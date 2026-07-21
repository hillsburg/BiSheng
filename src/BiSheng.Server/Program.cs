using System.Net;
using System.Threading.RateLimiting;
using BiSheng.Server.Api;
using BiSheng.Server.Data;
using BiSheng.Server.DependencyInjection;
using BiSheng.Server.Middleware;
using BiSheng.Server.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBiShengServerCore(
    builder.Configuration,
    builder.Environment.ContentRootPath);
builder.Services.AddBiShengBackgroundWorkers(builder.Configuration);

var corsSection = builder.Configuration.GetSection("Cors");
var allowedOrigins = corsSection.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
var allowAnyOrigin = corsSection.GetValue("AllowAnyOrigin", builder.Environment.IsDevelopment());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
        else if (allowAnyOrigin)
        {
            policy.AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownNetworks.Clear();

    if (builder.Environment.IsProduction())
    {
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);
    }
});

builder.Services.AddAntiforgery();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", context =>
    {
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter(
                context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var httpContext = context.HttpContext;
        var logger = httpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("BiSheng.Server.RateLimiting");
        logger.LogWarning(
            "请求被限流: {Method} {Path}, IP={RemoteIp}",
            httpContext.Request.Method,
            httpContext.Request.Path,
            httpContext.Connection.RemoteIpAddress);

        if (httpContext.Request.Path.StartsWithSegments("/admin/login"))
        {
            httpContext.Response.Redirect("/admin/login?rateLimited=1");
            return;
        }

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        httpContext.Response.ContentType = "application/problem+json";
        var problem = ApiProblemResults.RateLimited(httpContext);
        await httpContext.Response.WriteAsJsonAsync(problem.Value, cancellationToken);
    };
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMiddleware<ExceptionLoggingMiddleware>();
}
else
{
    app.UseExceptionHandler(errorApp =>
    {
        errorApp.Run(async context =>
        {
            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("BiSheng.Server.UnhandledException");
            var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
            if (ex != null)
            {
                logger.LogError(ex, "未处理异常 {Method} {Path}",
                    context.Request.Method, context.Request.Path);
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            var problem = ApiProblemResults.InternalError(null, context);
            await context.Response.WriteAsJsonAsync(problem.Value);
        });
    });
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await DatabaseMigration.ApplyAsync(db, MigrationIds.Initial);
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;");
}

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();
app.UseCors();
app.UseMiddleware<SetupMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapHub<BiSheng.Server.Hubs.SyncHub>("/hubs/sync");

// 运维探活：不要求登录；未 Setup 时亦放行（见 SetupMiddleware）
app.MapGet("/health", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        var canConnect = await db.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            return Results.Json(
                new { status = "unhealthy", database = "unreachable" },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            status = "ok",
            database = "ok",
            version = BiSheng.Server.Services.ServerUpdateCheckService.GetCurrentVersion()
        });
    }
    catch (Exception)
    {
        return Results.Json(
            new { status = "unhealthy", database = "error" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.Run();

// 暴露给 WebApplicationFactory<Program> 用于集成测试
public partial class Program { }
