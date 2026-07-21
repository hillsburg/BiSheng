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

    /// <summary>构造账号页</summary>
    public AccountModel(
        AppDbContext db,
        AdminPendingSessionService pendingSessions,
        TotpSecretProtector totpProtector,
        IWebHostEnvironment env,
        ILogger<AccountModel> logger)
    {
        _db = db;
        _pendingSessions = pendingSessions;
        _totpProtector = totpProtector;
        _env = env;
        _logger = logger;
    }

    /// <summary>当前用户名</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>开发环境跳过旧 TOTP 校验</summary>
    public bool IsDevelopment => _env.IsDevelopment();

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

        if (!SetupModel.IsIdentityPasswordHash(user.PasswordHash)
            || !SetupModel.VerifyPassword(CurrentPassword, user.PasswordHash))
        {
            ErrorMessage = "当前密码不正确";
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

        if (SetupModel.VerifyPassword(NewPassword, user.PasswordHash))
        {
            ErrorMessage = "新密码不能与当前密码相同";
            return Page();
        }

        user.PasswordHash = SetupModel.HashPassword(NewPassword);
        await _db.SaveChangesAsync();
        _logger.LogInformation("管理员 {Username} 已修改密码", user.Username);
        TempData["AccountSuccess"] = "密码已更新";
        return Redirect("/admin/account");
    }

    /// <summary>开始重绑：校验当前凭据后生成新密钥</summary>
    public async Task<IActionResult> OnPostStartTotpRebindAsync()
    {
        var user = await LoadUserAsync();
        if (user == null)
        {
            return Redirect("/admin/login");
        }

        if (!SetupModel.IsIdentityPasswordHash(user.PasswordHash)
            || !SetupModel.VerifyPassword(TotpCurrentPassword, user.PasswordHash))
        {
            ErrorMessage = "当前密码不正确";
            return Page();
        }

        if (!IsDevelopment)
        {
            var plain = _totpProtector.Unprotect(user.TotpSecret);
            if (!TotpHelper.VerifyCode(plain, CurrentTotpCode))
            {
                ErrorMessage = "当前验证码错误，请检查 Authenticator App";
                return Page();
            }
        }

        var newSecret = TotpHelper.GenerateSecret();
        _pendingSessions.SetTotpRebindPending(HttpContext, new TotpRebindPendingState
        {
            UserId = user.Id,
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
            || pending.UserId != user.Id)
        {
            ErrorMessage = "重绑会话已过期，请重新开始";
            _pendingSessions.ClearTotpRebindPending(HttpContext);
            return Page();
        }

        DisplayTotpSecret = pending.TotpSecret;
        QrCodeUrl = TotpHelper.GetQrCodeDataUri(
            TotpHelper.GetOtpAuthUri(pending.TotpSecret, user.Username));
        IsTotpRebindStep = true;

        if (!TotpHelper.VerifyCode(pending.TotpSecret, NewTotpCode))
        {
            ErrorMessage = "新验证码错误，请用新二维码对应的 App 重试";
            return Page();
        }

        user.TotpSecret = _totpProtector.Protect(pending.TotpSecret);
        await _db.SaveChangesAsync();
        _pendingSessions.ClearTotpRebindPending(HttpContext);
        _logger.LogInformation("管理员 {Username} 已重绑 TOTP", user.Username);
        TempData["AccountSuccess"] = "两步验证已重新绑定，请确认旧条目已从 Authenticator 中删除";
        return Redirect("/admin/account");
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
