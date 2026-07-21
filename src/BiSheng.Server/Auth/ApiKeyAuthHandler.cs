using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Auth;

/// <summary>
/// 自定义认证处理器：通过 X-Api-Key Header 或 Query/Bearer 验证 API Key
/// REST 客户端使用 X-Api-Key；SignalR 客户端通过 AccessTokenProvider 传递 access_token 或 Bearer
/// Key 以 SHA256 哈希存储在数据库中
/// </summary>
public class ApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string QueryName = "api_key";
    /// <summary>SignalR WebSocket 默认 query 参数名</summary>
    public const string AccessTokenQueryName = "access_token";

    private readonly IServiceScopeFactory _scopeFactory;

    public ApiKeyAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServiceScopeFactory scopeFactory)
        : base(options, logger, encoder)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var apiKey = ExtractApiKey();

        if (string.IsNullOrEmpty(apiKey))
            return AuthenticateResult.NoResult();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 计算传入 Key 的哈希值
        var keyHash = HashApiKey(apiKey);

        // 哈希匹配
        var key = await db.ApiKeys
            .Include(k => k.User)
            .FirstOrDefaultAsync(k => k.KeyValue == keyHash && k.IsActive);

        if (key == null)
        {
            // 不记录 QueryString，避免 access_token 进入日志
            Logger.LogWarning(
                "API Key 认证失败: {RemoteIp} {Method} {Path}",
                Context.Connection.RemoteIpAddress,
                Request.Method,
                Request.Path);
            return AuthenticateResult.Fail("Invalid or inactive API key.");
        }

        // 节流更新 LastUsedAt，避免每次请求都写库
        var now = DateTime.UtcNow;
        if (key.LastUsedAt is null || now - key.LastUsedAt.Value > TimeSpan.FromMinutes(5))
        {
            key.LastUsedAt = now;
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "更新 ApiKey.LastUsedAt 失败（不影响认证）");
            }
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, key.UserId.ToString()),
            new Claim(ClaimTypes.Name, key.User.Username),
            new Claim(BiShengClaimTypes.DeviceName, key.DeviceName ?? string.Empty),
            new Claim(BiShengClaimTypes.ApiKeyId, key.Id.ToString()),
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// 从 Header / Hub query 提取 API Key。
    /// REST /api/* 仅接受 X-Api-Key。
    /// SignalR WebSocket 协商仍依赖 access_token query（框架约定）；鉴权失败日志只记 Path 不记 Query。
    /// </summary>
    private string? ExtractApiKey()
    {
        var apiKey = Request.Headers[HeaderName].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        var path = Request.Path.Value ?? string.Empty;
        var isHubPath = path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase);
        if (!isHubPath)
        {
            return null;
        }

        apiKey = Request.Query[AccessTokenQueryName].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKey))
        {
            return apiKey;
        }

        var authHeader = Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authHeader["Bearer ".Length..].Trim();
        }

        return null;
    }

    /// <summary>
    /// 对 API Key 计算 SHA256 哈希（用于存储和比对）
    /// </summary>
    internal static string HashApiKey(string apiKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
