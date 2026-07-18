namespace BiSheng.Server.Middleware;

/// <summary>
/// 捕获并记录未处理异常后重新抛出，便于开发环境保留开发者异常页
/// </summary>
public class ExceptionLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionLoggingMiddleware> _logger;

    public ExceptionLoggingMiddleware(RequestDelegate next, ILogger<ExceptionLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常 {Method} {Path}",
                context.Request.Method, context.Request.Path);
            throw;
        }
    }
}
