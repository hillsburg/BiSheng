using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

/// <summary>账号安全：改密、重绑 TOTP</summary>
[AdminPanelAuthorize]
[EnableRateLimiting("login")]
public class AccountModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminPendingSessionService _pendingSessions;
    private readonly TotpSecretProtector _totpProtector;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<AccountModel> _logger;
    private readonly AdminCredentialService _credentials;
    private readonly AdminSessionService _sessions;

    /// <summary>构造账号页</summary>
    public AccountModel(
        AppDbContext db,
        AdminPendingSessionService pendingSessions,
        TotpSecretProtector totpProtector,
        IWebHostEnvironment env,
        ILogger<AccountModel> logger, AdminCredentialService credentials, AdminSessionService sessions)
    {
        _db = db;
        _pendingSessions = pendingSessions;
        _totpProtector = totpProtector;
        _env = env;
        _logger = logger;
        _credentials = credentials;
        _sessions = sessions;
    }

    /// <summary>当前用户名</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>开发环境跳过旧 TOTP 校验</summary>
    public bool IsDevelopment => _env.IsDevelopment();
    public bool NeedsTotp => !IsDevelopment && !AdminSessionService.HasRecentVerification(User);
    [BindProperty]
    public string PasswordChangeTotpCode { get; set; } = string.Empty;

    /// <summary>是否处于重绑 TOTP 扫码步骤</summary>
    public bool IsTotpRebindStep { get; set; }

    /// <summary>新 TOTP 密钥（仅重绑步骤展示）</summary>
    public string? DisplayTotpSecret { get; set; }

    /// <summary>新 TOTP 二维码 Data URI</summary>
    public string? QrCodeUrl { get; set; }

    /// <summary>成功提示</summary>
    public string SuccessMessage { get; set; } = string.Empty;

    /// <summary>错误提示</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>是否因 POST 过频被限流</summary>
    public bool IsRateLimited => Request.Query.ContainsKey("rateLimited");

    /// <summary>限流提示文案（与登录页同策略：5 次/分钟/IP）</summary>
    public const string RateLimitMessage = "操作过于频繁，请约 1 分钟后再试";

    /// <summary>改密：当前密码</summary>
    [BindProperty]
    public string CurrentPassword { get; set; } = string.Empty;

    /// <summary>改密：新密码</summary>
    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    /// <summary>改密：确认新密码</summary>
    [BindProperty]
    public string ConfirmNewPassword { get; set; } = string.Empty;

    /// <summary>重绑：当前密码</summary>
    [BindProperty]
    public string TotpCurrentPassword { get; set; } = string.Empty;

    /// <summary>重绑：当前 Authenticator 验证码（生产必填）</summary>
    [BindProperty]
    public string CurrentTotpCode { get; set; } = string.Empty;

    /// <summary>重绑：新 Authenticator 验证码</summary>
    [BindProperty]
    public string NewTotpCode { get; set; } = string.Empty;

    /// <summary>GET：加载账号信息；可取消重绑</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        if (Request.Query.ContainsKey("resetTotp"))
        {
            _pendingSessions.ClearTotpRebindPending(HttpContext);
            return Redirect("/admin/account");
        }

        await LoadUserAsync();
        RestoreTotpRebindStep();
        SuccessMessage = TempData["AccountSuccess"] as string ?? string.Empty;
        return Page();
    }

    /// <summary>修改密码</summary>
    public async Task<IActionResult> OnPostChangePasswordAsync()
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return Redirect("/admin/login");
        }

        RestoreTotpRebindStep();

        if (!await _credentials.AuthorizeSensitiveOperationAsync(HttpContext, user, CurrentPassword, PasswordChangeTotpCode, IsDevelopment))
        {
            ErrorMessage = "凭据错误、验证码已使用，或账户暂时锁定";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(NewPassword) || NewPassword.Length < 6)
        {
            ErrorMessage = "新密码至少 6 个字符";
            return Page();
        }

        if (NewPassword != ConfirmNewPassword)
        {
            ErrorMessage = "两次输入的新密码不一致";
            return Page();
        }

        if (PasswordCredentialService.VerifyPassword(NewPassword, user.PasswordHash))
        {
            ErrorMessage = "新密码不能与当前密码相同";
            return Page();
        }

        user.PasswordHash = PasswordCredentialService.HashPassword(NewPassword);
        user.AuthVersion++;
        if (!await SaveCredentialsAsync())
        {
            return Redirect("/admin/login");
        }
        await _sessions.SignOutAsync(HttpContext);
        _logger.LogInformation("管理员 {Username} 已修改密码", user.Username);
        TempData["AccountSuccess"] = "密码已更新";
        return Redirect("/admin/login");
    }

    /// <summary>开始重绑：校验当前凭据后生成新密钥</summary>
    public async Task<IActionResult> OnPostStartTotpRebindAsync()
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return Redirect("/admin/login");
        }

        if (!await _credentials.AuthorizeSensitiveOperationAsync(HttpContext, user, TotpCurrentPassword, CurrentTotpCode, IsDevelopment))
        {
            ErrorMessage = "凭据错误、验证码已使用，或账户暂时锁定";
            return Page();
        }

        var newSecret = TotpHelper.GenerateSecret();
        _pendingSessions.SetTotpRebindPending(HttpContext, new TotpRebindPendingState
        {
            UserId = user.Id,
            AuthVersion = user.AuthVersion,
            TotpSecret = newSecret
        });

        DisplayTotpSecret = newSecret;
        QrCodeUrl = TotpHelper.GetQrCodeDataUri(
            TotpHelper.GetOtpAuthUri(newSecret, user.Username));
        IsTotpRebindStep = true;
        return Page();
    }

    /// <summary>确认重绑：校验新码并落库</summary>
    public async Task<IActionResult> OnPostConfirmTotpRebindAsync()
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return Redirect("/admin/login");
        }

        if (!_pendingSessions.TryGetTotpRebindPending(HttpContext, out var pending)
            || pending.UserId != user.Id || pending.AuthVersion != user.AuthVersion)
        {
            ErrorMessage = "重绑会话已过期，请重新开始";
            _pendingSessions.ClearTotpRebindPending(HttpContext);
            return Page();
        }

        DisplayTotpSecret = pending.TotpSecret;
        QrCodeUrl = TotpHelper.GetQrCodeDataUri(
            TotpHelper.GetOtpAuthUri(pending.TotpSecret, user.Username));
        IsTotpRebindStep = true;

        if (!_pendingSessions.BeginAttempt(HttpContext, AdminPendingSessionService.TotpRebindCookieName, pending)
            || !TotpHelper.TryVerifyCode(pending.TotpSecret, NewTotpCode, out var timeStep))
        {
            ErrorMessage = "新验证码错误，请用新二维码对应的 App 重试";
            return Page();
        }

        if (!_pendingSessions.ConsumeTotpRebindPending(HttpContext, pending))
        {
            return Redirect("/admin/account");
        }
        user.TotpSecret = _totpProtector.Protect(pending.TotpSecret);
        user.LastTotpTimeStep = timeStep;
        user.AuthVersion++;
        if (!await SaveCredentialsAsync())
        {
            return Redirect("/admin/login");
        }
        await _sessions.SignOutAsync(HttpContext);
        _pendingSessions.ClearTotpRebindPending(HttpContext);
        _logger.LogInformation("管理员 {Username} 已重绑 TOTP", user.Username);
        TempData["AccountSuccess"] = "两步验证已重新绑定，请确认旧条目已从 Authenticator 中删除";
        return Redirect("/admin/login");
    }

    /// <summary>重新验证后提高认证版本，撤销当前账号的全部后台会话。</summary>
    public async Task<IActionResult> OnPostRevokeSessionsAsync()
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return Redirect("/admin/login");
        }
        if (!await _credentials.AuthorizeSensitiveOperationAsync(HttpContext, user, CurrentPassword, PasswordChangeTotpCode, IsDevelopment))
        {
            ErrorMessage = "凭据错误、验证码已使用，或账户暂时锁定";
            return Page();
        }
        user.AuthVersion++;
        await SaveCredentialsAsync();
        await _sessions.SignOutAsync(HttpContext);
        return Redirect("/admin/login");
    }

    /// <summary>保存凭据变更；并发更新冲突时退出当前会话，防止覆盖新凭据。</summary>
    private async Task<bool> SaveCredentialsAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            await _sessions.SignOutAsync(HttpContext);
            return false;
        }
    }

    private async Task<Data.Entities.User?> LoadUserAsync()
    {
        var userId = User.GetUserId();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        Username = user?.Username ?? User.Identity?.Name ?? string.Empty;
        return user;
    }

    private void RestoreTotpRebindStep()
    {
        if (!_pendingSessions.TryGetTotpRebindPending(HttpContext, out var pending))
        {
            return;
        }

        if (pending.UserId != User.GetUserId())
        {
            _pendingSessions.ClearTotpRebindPending(HttpContext);
            return;
        }

        DisplayTotpSecret = pending.TotpSecret;
        QrCodeUrl = TotpHelper.GetQrCodeDataUri(
            TotpHelper.GetOtpAuthUri(pending.TotpSecret, Username));
        IsTotpRebindStep = true;
    }
}
