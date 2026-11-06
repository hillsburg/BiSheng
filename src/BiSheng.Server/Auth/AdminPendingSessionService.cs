using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace BiSheng.Server.Auth;

/// <summary>保存单实例、有容量限制的短时授权状态；重启后待完成流程失效。</summary>
public sealed class AdminPendingSessionService
{
    public const string LoginCookieName = "bisheng.admin.pending-login";
    public const string SetupCookieName = "bisheng.admin.pending-setup";
    public const string TotpRebindCookieName = "bisheng.admin.pending-totp-rebind";
    public static readonly TimeSpan LoginPendingTimeToLive = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan SetupPendingTimeToLive = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan TotpRebindPendingTimeToLive = TimeSpan.FromMinutes(5);
    public const string PasskeyLoginCookieName = "bisheng.admin.passkey-login";
    public const string PasskeyRegisterCookieName = "bisheng.admin.passkey-register";

    private readonly Dictionary<string, PendingState> _states = new();
    private readonly object _gate = new();
    private readonly IDataProtectionProvider _protection;
    private readonly bool _secure;

    /// <summary>保存单实例、有容量限制的短时授权状态；重启后待完成流程失效。</summary>
    public AdminPendingSessionService(IDataProtectionProvider protection, IConfiguration configuration)
    {
        _protection = protection;
        _secure = configuration.GetValue("Cookies:SecureAlways", false);
    }

    /// <summary>保存密码验证后的登录授权及对应认证版本。</summary>
    public void SetLoginPending(HttpContext context, Guid id, string username, long authVersion = 1) =>
        Set(context, LoginCookieName, new LoginPendingPayload { UserId = id, Username = username, AuthVersion = authVersion }, LoginPendingTimeToLive);
    /// <summary>读取尚未过期的登录授权，不消费状态。</summary>
    public bool TryGetLoginPending(HttpContext context, out LoginPendingPayload state) => Get(context, LoginCookieName, out state);
    /// <summary>成功登录前原子消费授权，禁止 Cookie 重放。</summary>
    public bool ConsumeLoginPending(HttpContext context, LoginPendingPayload state) => Consume(context, LoginCookieName, state);
    /// <summary>撤销登录授权并删除对应 Cookie。</summary>
    public void ClearLoginPending(HttpContext context) => Clear(context, LoginCookieName);
    /// <summary>保存初始化凭据，敏感信息仅保留在服务端内存。</summary>
    public void SetSetupPending(HttpContext context, SetupPendingState state) => Set(context, SetupCookieName, state, SetupPendingTimeToLive);
    /// <summary>读取尚未过期的初始化状态。</summary>
    public bool TryGetSetupPending(HttpContext context, out SetupPendingState state) => Get(context, SetupCookieName, out state);
    /// <summary>创建账号前原子消费初始化授权。</summary>
    public bool ConsumeSetupPending(HttpContext context, SetupPendingState state) => Consume(context, SetupCookieName, state);
    /// <summary>撤销初始化授权并删除对应 Cookie。</summary>
    public void ClearSetupPending(HttpContext context) => Clear(context, SetupCookieName);
    /// <summary>保存经过重新验证的重绑授权和新密钥。</summary>
    public void SetTotpRebindPending(HttpContext context, TotpRebindPendingState state) => Set(context, TotpRebindCookieName, state, TotpRebindPendingTimeToLive);
    /// <summary>读取尚未过期的重绑授权。</summary>
    public bool TryGetTotpRebindPending(HttpContext context, out TotpRebindPendingState state) => Get(context, TotpRebindCookieName, out state);
    /// <summary>更新密钥前原子消费重绑授权。</summary>
    public bool ConsumeTotpRebindPending(HttpContext context, TotpRebindPendingState state) => Consume(context, TotpRebindCookieName, state);
    /// <summary>撤销重绑授权并删除对应 Cookie。</summary>
    public void ClearTotpRebindPending(HttpContext context) => Clear(context, TotpRebindCookieName);
    /// <summary>保存匿名登录的 WebAuthn 挑战。</summary>
    public void SetPasskeyLoginPending(HttpContext context, PasskeyLoginState state) =>
        Set(context, PasskeyLoginCookieName, state, WebAuthnOptions.ChallengeTimeToLive);
    /// <summary>原子提取并消费登录挑战；验证失败也不能重放。</summary>
    public bool TakePasskeyLoginPending(HttpContext context, out PasskeyLoginState state) =>
        Take(context, PasskeyLoginCookieName, out state);
    /// <summary>保存已重新授权的绑定挑战。</summary>
    public void SetPasskeyRegisterPending(HttpContext context, PasskeyRegisterState state) =>
        Set(context, PasskeyRegisterCookieName, state, WebAuthnOptions.ChallengeTimeToLive);
    /// <summary>原子提取并消费绑定挑战；验证失败也不能重放。</summary>
    public bool TakePasskeyRegisterPending(HttpContext context, out PasskeyRegisterState state) =>
        Take(context, PasskeyRegisterCookieName, out state);

    /// <summary>在锁内扣减验证次数预算，避免并发绕过次数限制。</summary>
    public bool BeginAttempt(HttpContext context, string name, PendingState expected)
    {
        lock (_gate)
        {
            var id = ReadId(context, name);
            if (id == null || !_states.TryGetValue(id, out var state) || !ReferenceEquals(state, expected)
                || state.ExpiresUtc <= DateTime.UtcNow)
            {
                return false;
            }
            if (++state.Attempts <= 5)
            {
                return true;
            }
            Clear(context, name);
            return false;
        }
    }

    /// <summary>替换当前浏览器的授权，清理过期项并限制内存容量。</summary>
    private void Set(HttpContext context, string name, PendingState state, TimeSpan ttl)
    {
        lock (_gate)
        {
            Clear(context, name, deleteCookie: false);
            foreach (var key in _states.Where(p => p.Value.ExpiresUtc <= DateTime.UtcNow).Select(p => p.Key).ToArray())
            {
                _states.Remove(key);
            }
            // 限制匿名初始化请求能够占用的内存；容量满时淘汰最早到期项。
            if (_states.Count >= 1024)
            {
                _states.Remove(_states.MinBy(p => p.Value.ExpiresUtc).Key);
            }
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            state.ExpiresUtc = DateTime.UtcNow.Add(ttl);
            _states.Add(id, state);
            context.Response.Cookies.Append(name, _protection.CreateProtector(name + ".v2").Protect(id), new CookieOptions
            {
                HttpOnly = true, Secure = _secure || context.Request.IsHttps, SameSite = SameSiteMode.Lax,
                IsEssential = true, MaxAge = ttl, Path = "/"
            });
        }
    }

    /// <summary>在锁内读取类型及有效期均匹配的授权。</summary>
    private bool Get<T>(HttpContext context, string name, out T state) where T : PendingState
    {
        lock (_gate)
        {
            state = null!;
            var id = ReadId(context, name);
            if (id != null && _states.TryGetValue(id, out var stored) && stored is T typed && stored.ExpiresUtc > DateTime.UtcNow)
            {
                state = typed;
                return true;
            }
            Clear(context, name);
            return false;
        }
    }

    /// <summary>仅消费读取时的同一状态实例，确保至多成功一次。</summary>
    private bool Consume(HttpContext context, string name, PendingState expected)
    {
        lock (_gate)
        {
            var id = ReadId(context, name);
            if (id == null || !_states.TryGetValue(id, out var state) || !ReferenceEquals(state, expected)
                || state.ExpiresUtc <= DateTime.UtcNow)
            {
                return false;
            }
            Clear(context, name);
            return true;
        }
    }

    /// <summary>在同一把锁内读取并消费，供不计尝试次数的一次性挑战使用。</summary>
    private bool Take<T>(HttpContext context, string name, out T state) where T : PendingState
    {
        lock (_gate)
        {
            return Get(context, name, out state) && Consume(context, name, state);
        }
    }

    /// <summary>同步撤销服务端状态，可按需删除浏览器 Cookie。</summary>
    private void Clear(HttpContext context, string name, bool deleteCookie = true)
    {
        lock (_gate)
        {
            var id = ReadId(context, name);
            if (id != null)
            {
                _states.Remove(id);
            }
            if (deleteCookie)
            {
                context.Response.Cookies.Delete(name, new CookieOptions { Path = "/", SameSite = SameSiteMode.Lax });
            }
        }
    }

    /// <summary>按流程独立的保护用途解密会话标识，篡改视为无效。</summary>
    private string? ReadId(HttpContext context, string name)
    {
        if (!context.Request.Cookies.TryGetValue(name, out var value))
        {
            return null;
        }
        try
        {
            return _protection.CreateProtector(name + ".v2").Unprotect(value);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>待验证流程共享的有效期和尝试次数。</summary>
public abstract class PendingState
{
    public DateTime ExpiresUtc { get; set; }
    public int Attempts { get; set; }
}
/// <summary>密码已通过验证的登录授权，不包含任何密码或 TOTP 密钥。</summary>
public sealed class LoginPendingPayload : PendingState
{
    public Guid UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public long AuthVersion { get; set; }
}
/// <summary>初始化期间仅保留在内存中的账号凭据。</summary>
public sealed class SetupPendingState : PendingState
{
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string TotpSecret { get; set; } = string.Empty;
}
/// <summary>绑定用户及认证版本的重绑授权。</summary>
public sealed class TotpRebindPendingState : PendingState
{
    public Guid UserId { get; set; }
    public long AuthVersion { get; set; }
    public string TotpSecret { get; set; } = string.Empty;
}

/// <summary>匿名登录挑战的原始参数，仅保存在服务端。</summary>
public sealed class PasskeyLoginState : PendingState
{
    public string OptionsJson { get; set; } = string.Empty;
}
/// <summary>绑定挑战的原始参数及授权用户快照，仅保存在服务端。</summary>
public sealed class PasskeyRegisterState : PendingState
{
    public string OptionsJson { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public long AuthVersion { get; set; }
    public string Name { get; set; } = string.Empty;
}
