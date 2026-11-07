using System.Globalization;
using System.Security.Claims;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Auth;

/// <summary>管理后台 Cookie 的签发、撤销检查及固定的近期验证时间。</summary>
public sealed class AdminSessionService(AppDbContext db)
{
    public const string VersionClaim = "bisheng.auth-version";
    public const string VerifiedAtClaim = "bisheng.verified-at";
    /// <summary>完成验证后免输 TOTP 的时间窗口。</summary>
    public static readonly TimeSpan RecentVerificationWindow = TimeSpan.FromMinutes(5);

    /// <summary>完整验证后签发后台 Cookie，并以当前时间作为验证时间。</summary>
    public Task SignInAsync(HttpContext context, User user) =>
        IssueAsync(context, user, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    /// <summary>认证版本变化后为当前会话换发 Cookie，沿用原验证时间，不延长免 TOTP 窗口。</summary>
    public Task RefreshAsync(HttpContext context, User user)
    {
        // 缺少验证时间的旧会话按"已过期"处理，后续敏感操作仍要求 TOTP。
        var verifiedAt = long.TryParse(context.User.FindFirstValue(VerifiedAtClaim), out var timestamp) ? timestamp : 0;
        return IssueAsync(context, user, verifiedAt);
    }

    /// <summary>签发包含认证版本和指定验证时间的后台 Cookie。</summary>
    private static Task IssueAsync(HttpContext context, User user, long verifiedAt) => context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, AuthRoles.Admin),
            new Claim(VersionClaim, user.AuthVersion.ToString(CultureInfo.InvariantCulture)),
            new Claim(VerifiedAtClaim, verifiedAt.ToString(CultureInfo.InvariantCulture))
        }, CookieAuthenticationDefaults.AuthenticationScheme)));

    /// <summary>清除当前浏览器的后台认证 Cookie。</summary>
    public Task SignOutAsync(HttpContext context) =>
        context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>判断是否在五分钟内完成验证；Cookie 滑动续期不会延长该窗口。</summary>
    public static bool HasRecentVerification(ClaimsPrincipal principal)
    {
        if (!long.TryParse(principal.FindFirstValue(VerifiedAtClaim), out var timestamp))
        {
            return false;
        }
        var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp;
        return age >= 0 && age <= (long)RecentVerificationWindow.TotalSeconds;
    }

    /// <summary>每次请求对照数据库认证版本，拒绝旧会话及缺少版本的旧 Cookie。</summary>
    public async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (Guid.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            && long.TryParse(principal?.FindFirstValue(VersionClaim), out var version)
            && await db.Users.AsNoTracking().AnyAsync(u => u.Id == id && u.AuthVersion == version))
        {
            return;
        }

        context.RejectPrincipal();
        await SignOutAsync(context.HttpContext);
    }
}
