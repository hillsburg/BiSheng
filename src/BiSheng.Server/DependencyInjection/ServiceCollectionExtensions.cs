using BiSheng.Server.Api;
using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Services;
using BiSheng.Server.Services.Images;
using BiSheng.Server.Services.Mutations;
using BiSheng.Shared.Api;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.DependencyInjection;

/// <summary>
/// BiSheng Server 依赖注入扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册核心服务：数据库、认证、API 业务服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">应用配置</param>
    /// <param name="contentRootPath">内容根目录；用于默认 DP 密钥路径（适配 ProtectHome）</param>
    public static IServiceCollection AddBiShengServerCore(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRootPath = null)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(
                configuration.GetConnectionString("DefaultConnection")
                ?? ServerDatabasePaths.DefaultConnectionString));

        // 持久化 Data Protection 密钥，避免重启后管理端 cookie / 两步登录会话失效。
        // 默认放在 ContentRoot/data-protection-keys，便于 systemd ProtectHome=true。
        var dpKeysDir = configuration["DataProtection:KeysPath"];
        if (string.IsNullOrWhiteSpace(dpKeysDir))
        {
            var root = string.IsNullOrWhiteSpace(contentRootPath)
                ? Directory.GetCurrentDirectory()
                : contentRootPath;
            dpKeysDir = Path.Combine(root, "data-protection-keys");
        }

        Directory.CreateDirectory(dpKeysDir);
        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(dpKeysDir))
            .SetApplicationName("BiSheng.Server");

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                options.LoginPath = "/admin/login";
                options.LogoutPath = "/admin/logout";
                options.Cookie.Name = "bisheng.admin";
                options.Cookie.HttpOnly = true;
                // Production 强制 Secure；开发环境仍允许 HTTP 本机调试
                options.Cookie.SecurePolicy = configuration.GetValue("Cookies:SecureAlways", false)
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromDays(7);
                options.SlidingExpiration = true;
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthHandler>(
                ApiKeyAuthHandler.SchemeName, _ => { });

        services.AddAuthorization();
        // 管理后台两步登录 / 初始化的短时加密会话
        services.AddSingleton<AdminPendingSessionService>();
        services.AddSingleton<TotpSecretProtector>();
        services.AddSignalR();
        services.AddProblemDetails();
        services.AddControllers()
            .ConfigureApiBehaviorOptions(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var errors = context.ModelState
                        .Where(entry => entry.Value?.Errors.Count > 0)
                        .ToDictionary(
                            entry => entry.Key,
                            entry => entry.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

                    var problem = new ValidationProblemDetails(errors)
                    {
                        Type = $"{ApiErrorCodes.TypeBase}/validation-failed",
                        Title = "请求参数无效",
                        Status = StatusCodes.Status400BadRequest,
                        Instance = context.HttpContext.Request.Path
                    };
                    problem.Extensions["code"] = ApiErrorCodes.ValidationFailed;
                    problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
                    return new BadRequestObjectResult(problem);
                };
            });
        services.AddRazorPages();
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();

        // 无状态单例：可在 Scoped DbContext 上安全调用
        services.AddSingleton<ClientSyncStateService>();
        services.AddSingleton<NoteRevisionService>();
        services.AddSingleton<UserSyncVersionService>();
        // 每请求一个实例，持有 DbContext
        services.AddScoped<IEntityChangeWriter, EntityChangeWriter>();
        services.AddScoped<ISyncChangeNotifier, SyncChangeNotifier>();
        services.AddScoped<INoteMutationService, NoteMutationService>();
        services.AddScoped<IFolderMutationService, FolderMutationService>();
        services.AddScoped<ISyncService, SyncService>();

        return services;
    }

    /// <summary>
    /// 注册后台 HostedService（图片 GC、SyncLog 裁剪）与图片清理服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="configuration">应用配置（绑定 ImageGc）</param>
    public static IServiceCollection AddBiShengBackgroundWorkers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ImageCleanupOptions>(
            configuration.GetSection(ImageCleanupOptions.SectionName));
        services.AddScoped<ImageCleanupService>();
        services.AddHostedService<ImageGarbageCollector>();
        services.AddHostedService<SyncLogCompactionService>();
        return services;
    }
}
