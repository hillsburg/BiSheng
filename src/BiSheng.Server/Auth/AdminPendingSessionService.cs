using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Auth;

/// <summary>
/// 管理后台两步流程的短时会话：
/// 登录待会话仍用 Data Protection Cookie；
/// 初始化待会话仅在 Cookie 中放不透明 SessionId，密码哈希与 TOTP 明文只存服务端内存。
/// </summary>
public sealed class AdminPendingSessionService
{
    /// <summary>登录待 TOTP 会话 Cookie 名</summary>
    public const string LoginCookieName = "bisheng.admin.pending-login";

    /// <summary>初始化待完成会话 Cookie 名</summary>
    public const string SetupCookieName = "bisheng.admin.pending-setup";

    /// <summary>重绑 TOTP 待完成会话 Cookie 名</summary>
    public const string TotpRebindCookieName = "bisheng.admin.pending-totp-rebind";

    /// <summary>登录待会话默认有效期</summary>
    public static readonly TimeSpan LoginPendingTimeToLive = TimeSpan.FromMinutes(5);

    /// <summary>初始化待会话默认有效期</summary>
    public static readonly TimeSpan SetupPendingTimeToLive = TimeSpan.FromMinutes(15);

    /// <summary>重绑 TOTP 待会话默认有效期</summary>
    public static readonly TimeSpan TotpRebindPendingTimeToLive = TimeSpan.FromMinutes(15);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITimeLimitedDataProtector _loginProtector;
    private readonly ITimeLimitedDataProtector _setupProtector;
    private readonly ITimeLimitedDataProtector _totpRebindProtector;
    private readonly bool _secureAlways;
    private readonly ConcurrentDictionary<string, SetupPendingState> _setupStates = new();
    private readonly ConcurrentDictionary<string, TotpRebindPendingState> _totpRebindStates = new();

    /// <summary>构造短时会话服务</summary>
    public AdminPendingSessionService(
        IDataProtectionProvider dataProtection,
        IConfiguration configuration)
    {
        _loginProtector = dataProtection.CreateProtector("BiSheng.Admin.PendingLogin.v1")
            .ToTimeLimitedDataProtector();
        _setupProtector = dataProtection.CreateProtector("BiSheng.Admin.PendingSetup.v1")
            .ToTimeLimitedDataProtector();
        _totpRebindProtector = dataProtection.CreateProtector("BiSheng.Admin.PendingTotpRebind.v1")
            .ToTimeLimitedDataProtector();
        _secureAlways = configuration.GetValue("Cookies:SecureAlways", false);
    }

    /// <summary>密码验证通过后写入登录待会话</summary>
    public void SetLoginPending(HttpContext httpContext, Guid userId, string username)
    {
        var payload = new LoginPendingPayload
        {
            UserId = userId,
            Username = username
        };
        WriteCookie(
            httpContext,
            LoginCookieName,
            Protect(_loginProtector, payload, LoginPendingTimeToLive),
            LoginPendingTimeToLive);
    }

    /// <summary>读取并校验登录待会话；过期或不存在返回 false</summary>
    public bool TryGetLoginPending(HttpContext httpContext, out LoginPendingPayload payload)
    {
        payload = default!;
        if (!httpContext.Request.Cookies.TryGetValue(LoginCookieName, out var cookie)
            || string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        if (!TryUnprotect(_loginProtector, cookie, out LoginPendingPayload? data)
            || data == null
            || data.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(data.Username))
        {
            ClearLoginPending(httpContext);
            return false;
        }

        payload = data;
        return true;
    }

    /// <summary>清除登录待会话</summary>
    public void ClearLoginPending(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(LoginCookieName, BuildDeleteOptions());
    }

    /// <summary>
    /// 初始化第一步通过后写入待完成会话：
    /// Cookie 仅含 SessionId；密码以哈希、TOTP 明文仅存内存。
    /// </summary>
    public void SetSetupPending(HttpContext httpContext, SetupPendingState state)
    {
        CleanupExpiredSetupStates();
        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        state.ExpiresUtc = DateTime.UtcNow.Add(SetupPendingTimeToLive);
        _setupStates[sessionId] = state;

        var cookiePayload = new SetupPendingCookiePayload { SessionId = sessionId };
        WriteCookie(
            httpContext,
            SetupCookieName,
            Protect(_setupProtector, cookiePayload, SetupPendingTimeToLive),
            SetupPendingTimeToLive);
    }

    /// <summary>读取并校验初始化待会话（内存态）</summary>
    public bool TryGetSetupPending(HttpContext httpContext, out SetupPendingState state)
    {
        state = default!;
        if (!httpContext.Request.Cookies.TryGetValue(SetupCookieName, out var cookie)
            || string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        if (!TryUnprotect(_setupProtector, cookie, out SetupPendingCookiePayload? data)
            || data == null
            || string.IsNullOrWhiteSpace(data.SessionId))
        {
            ClearSetupPending(httpContext);
            return false;
        }

        if (!_setupStates.TryGetValue(data.SessionId, out var stored)
            || stored.ExpiresUtc < DateTime.UtcNow
            || string.IsNullOrWhiteSpace(stored.Username)
            || string.IsNullOrWhiteSpace(stored.PasswordHash)
            || string.IsNullOrWhiteSpace(stored.TotpSecret))
        {
            if (!string.IsNullOrWhiteSpace(data.SessionId))
            {
                _setupStates.TryRemove(data.SessionId, out _);
            }

            ClearSetupPending(httpContext);
            return false;
        }

        state = stored;
        return true;
    }

    /// <summary>清除初始化待会话（Cookie + 内存）</summary>
    public void ClearSetupPending(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(SetupCookieName, out var cookie)
            && !string.IsNullOrEmpty(cookie)
            && TryUnprotect(_setupProtector, cookie, out SetupPendingCookiePayload? data)
            && data != null
            && !string.IsNullOrWhiteSpace(data.SessionId))
        {
            _setupStates.TryRemove(data.SessionId, out _);
        }

        httpContext.Response.Cookies.Delete(SetupCookieName, BuildDeleteOptions());
    }

    /// <summary>
    /// 重绑 TOTP：密码（及旧码）验证通过后写入待完成会话；
    /// Cookie 仅含 SessionId，新密钥明文仅存内存。
    /// </summary>
    public void SetTotpRebindPending(HttpContext httpContext, TotpRebindPendingState state)
    {
        CleanupExpiredTotpRebindStates();
        var sessionId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        state.ExpiresUtc = DateTime.UtcNow.Add(TotpRebindPendingTimeToLive);
        _totpRebindStates[sessionId] = state;

        var cookiePayload = new SetupPendingCookiePayload { SessionId = sessionId };
        WriteCookie(
            httpContext,
            TotpRebindCookieName,
            Protect(_totpRebindProtector, cookiePayload, TotpRebindPendingTimeToLive),
            TotpRebindPendingTimeToLive);
    }

    /// <summary>读取并校验重绑 TOTP 待会话</summary>
    public bool TryGetTotpRebindPending(HttpContext httpContext, out TotpRebindPendingState state)
    {
        state = default!;
        if (!httpContext.Request.Cookies.TryGetValue(TotpRebindCookieName, out var cookie)
            || string.IsNullOrEmpty(cookie))
        {
            return false;
        }

        if (!TryUnprotect(_totpRebindProtector, cookie, out SetupPendingCookiePayload? data)
            || data == null
            || string.IsNullOrWhiteSpace(data.SessionId))
        {
            ClearTotpRebindPending(httpContext);
            return false;
        }

        if (!_totpRebindStates.TryGetValue(data.SessionId, out var stored)
            || stored.ExpiresUtc < DateTime.UtcNow
            || stored.UserId == Guid.Empty
            || string.IsNullOrWhiteSpace(stored.TotpSecret))
        {
            if (!string.IsNullOrWhiteSpace(data.SessionId))
            {
                _totpRebindStates.TryRemove(data.SessionId, out _);
            }

            ClearTotpRebindPending(httpContext);
            return false;
        }

        state = stored;
        return true;
    }

    /// <summary>清除重绑 TOTP 待会话</summary>
    public void ClearTotpRebindPending(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(TotpRebindCookieName, out var cookie)
            && !string.IsNullOrEmpty(cookie)
            && TryUnprotect(_totpRebindProtector, cookie, out SetupPendingCookiePayload? data)
            && data != null
            && !string.IsNullOrWhiteSpace(data.SessionId))
        {
            _totpRebindStates.TryRemove(data.SessionId, out _);
        }

        httpContext.Response.Cookies.Delete(TotpRebindCookieName, BuildDeleteOptions());
    }

    /// <summary>加密并附加过期时间</summary>
    private static string Protect<T>(ITimeLimitedDataProtector protector, T payload, TimeSpan ttl)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(protector.Protect(bytes, ttl));
    }

    /// <summary>解密；过期或篡改返回 false</summary>
    private static bool TryUnprotect<T>(ITimeLimitedDataProtector protector, string cookie, out T? payload)
        where T : class
    {
        payload = null;
        try
        {
            var bytes = protector.Unprotect(Convert.FromBase64String(cookie));
            payload = JsonSerializer.Deserialize<T>(Encoding.UTF8.GetString(bytes), JsonOptions);
            return payload != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>写入 HttpOnly Cookie</summary>
    private void WriteCookie(HttpContext httpContext, string name, string value, TimeSpan ttl)
    {
        httpContext.Response.Cookies.Append(name, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = _secureAlways || httpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = ttl,
            Path = "/"
        });
    }

    /// <summary>删除 Cookie 时使用的选项（Path 需与写入一致）</summary>
    private static CookieOptions BuildDeleteOptions() => new()
    {
        Path = "/",
        SameSite = SameSiteMode.Lax
    };

    /// <summary>清理过期的初始化内存态</summary>
    private void CleanupExpiredSetupStates()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _setupStates)
        {
            if (pair.Value.ExpiresUtc < now)
            {
                _setupStates.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <summary>清理过期的重绑 TOTP 内存态</summary>
    private void CleanupExpiredTotpRebindStates()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in _totpRebindStates)
        {
            if (pair.Value.ExpiresUtc < now)
            {
                _totpRebindStates.TryRemove(pair.Key, out _);
            }
        }
    }
}

/// <summary>登录第二步（TOTP）所需的短时载荷</summary>
public sealed class LoginPendingPayload
{
    /// <summary>已通过密码校验的用户 ID</summary>
    public Guid UserId { get; set; }

    /// <summary>用户名（仅展示）</summary>
    public string Username { get; set; } = string.Empty;
}

/// <summary>初始化 / 重绑 Cookie 载荷：仅不透明会话 ID</summary>
public sealed class SetupPendingCookiePayload
{
    /// <summary>服务端内存会话键</summary>
    public string SessionId { get; set; } = string.Empty;
}

/// <summary>初始化第二步内存态（不含明文密码）</summary>
public sealed class SetupPendingState
{
    /// <summary>管理员用户名</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>第一步已计算的密码哈希</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>首个设备名称</summary>
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>服务端生成的 TOTP 明文密钥（仅存内存至完成/过期）</summary>
    public string TotpSecret { get; set; } = string.Empty;

    /// <summary>过期时间（UTC）</summary>
    public DateTime ExpiresUtc { get; set; }
}

/// <summary>重绑 TOTP 第二步内存态</summary>
public sealed class TotpRebindPendingState
{
    /// <summary>当前管理员用户 ID</summary>
    public Guid UserId { get; set; }

    /// <summary>新生成的 TOTP 明文密钥</summary>
    public string TotpSecret { get; set; } = string.Empty;

    /// <summary>过期时间（UTC）</summary>
    public DateTime ExpiresUtc { get; set; }
}
