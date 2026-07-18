using BiSheng.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Middleware;

/// <summary>
/// 首次部署时，强制所有请求跳转到 /admin/setup 页面
/// </summary>
public class SetupMiddleware
{
    private readonly RequestDelegate _next;

    public SetupMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        // 检查是否已完成初始化
        var isSetup = await db.ServerConfigs.AnyAsync(c => c.Id == 1 && c.IsSetup);

        if (isSetup)
        {
            await _next(context);
            return;
        }

        // 未初始化：只允许 /admin/setup 路径
        var path = context.Request.Path.Value?.ToLower() ?? string.Empty;

        if (path == "/admin/setup" || path.StartsWith("/_content") || path.StartsWith("/css") || path.StartsWith("/js"))
        {
            await _next(context);
            return;
        }

        // 其他所有请求重定向到 /admin/setup
        context.Response.Redirect("/admin/setup");
    }
}
