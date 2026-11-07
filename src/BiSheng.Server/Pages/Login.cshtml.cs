using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

[EnableRateLimiting("login")]
public class LoginModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly AdminPendingSessionService _pendingSessions;
    private readonly TotpSecretProtector _totpProtector;
    private readonly ILogger<LoginModel> _logger;
    private readonly AdminCredentialService _credentials;
    private readonly AdminSessionService _sessions;

    /// <summary>构造登录页模型</summary>
    public LoginModel(
        AppDbContext db,
        IWebHostEnvironment env,
        AdminPendingSessionService pendingSessions,
        TotpSecretProtector totpProtector,
        ILogger<LoginModel> logger, AdminCredentialService credentials, AdminSessionService sessions)
    {
        _db = db;
        _env = env;
        _pendingSessions = pendingSessions;
        _totpProtector = totpProtector;
        _logger = logger;
        _credentials = credentials;
        _sessions = sessions;
    }

    /// <summary>
    /// 是否为开发调试环境（跳过 TOTP 验证）
    /// </summary>
    public bool IsDevelopment => _env.IsDevelopment();

    /// <summary>用户名（第一步）</summary>
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    /// <summary>密码（第一步）</summary>
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// 第二步 TOTP 验证码
    /// </summary>
    [BindProperty]
    public string TotpCode { get; set; } = string.Empty;

    /// <summary>
    /// 暂存的用户名，用于第二步页面展示（来自服务端会话，非客户端提交）
    /// </summary>
    public string PendingUsername { get; set; } = string.Empty;

    /// <summary>错误提示</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>是否刚完成初始化</summary>
    public bool SetupDone => Request.Query.ContainsKey("setup");

    /// <summary>初始化时生成的首把 API Key（TempData Peek，登录后带到 Keys 页）</summary>
    public string? SetupApiKey { get; private set; }

    /// <summary>首把 Key 的设备名</summary>
    public string? SetupDeviceName { get; private set; }

    /// <summary>是否因登录 POST 过频被限流（由 OnRejected 重定向带入）</summary>
    public bool IsRateLimited => Request.Query.ContainsKey("rateLimited");

    /// <summary>限流提示文案</summary>
    public const string RateLimitMessage = "登录尝试过于频繁，请约 1 分钟后再试";

    /// <summary>
    /// 是否处于第二步（TOTP 验证阶段）：仅当服务端短时会话有效
    /// </summary>
    public bool IsTotpStep { get; private set; }

    /// <summary>GET：已登录跳转；带 reset 时清除待会话；有效待会话则展示 TOTP 步</summary>
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            // 已登录仍保留首 Key，直接带到 Keys 页展示
            if (TempData.Peek("NewApiKey") is string)
            {
                TempData.Keep("NewApiKey");
                TempData.Keep("DeviceName");
                return Redirect("/admin/keys");
            }

            return Redirect("/admin");
        }

        // 用户点击「返回登录」时清除待会话
        if (Request.Query.ContainsKey("reset"))
        {
            _pendingSessions.ClearLoginPending(HttpContext);
            return Redirect("/admin/login");
        }

        CaptureSetupApiKeyFromTempData();
        RestoreTotpStepFromSession();
        return Page();
    }

    /// <summary>POST：第一步验密或第二步验 TOTP</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        // POST 重渲染时继续保留首 Key，避免登录过程中 TempData 丢失
        CaptureSetupApiKeyFromTempData();

        // ===== 第二步：仅信任服务端 pending Cookie，忽略客户端伪造的 userId =====
        if (_pendingSessions.TryGetLoginPending(HttpContext, out var pending))
        {
            return await VerifyTotp(pending);
        }

        // ===== 第一步：校验用户名 + 密码 =====
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == Username);
        if (!await _credentials.VerifyPasswordAsync(user, Password))
        {
            _logger.LogWarning("后台登录失败，IP={RemoteIp}", HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "用户名或密码错误，或账户暂时锁定";
            return Page();
        }

        // 开发调试环境：跳过 TOTP，直接登录
        if (_env.IsDevelopment())
        {
            return await SignInUser(user!);
        }

        // 生产 / Test：密码验证通过，写入短时会话后进入 TOTP 步骤
        var plainTotp = _totpProtector.Unprotect(user!.TotpSecret);
        if (string.IsNullOrWhiteSpace(plainTotp))
        {
            _logger.LogError(
                "用户 {Username} 的 TotpSecret 为空或无法解密，无法完成生产登录",
                user.Username);
            ErrorMessage = "该账户尚未绑定两步验证，请重新完成初始化或联系管理员配置 TOTP。";
            return Page();
        }

        _pendingSessions.SetLoginPending(HttpContext, user.Id, user.Username, user.AuthVersion);
        PendingUsername = user.Username;
        IsTotpStep = true;
        return Page();
    }

    /// <summary>
    /// 第二步：校验 TOTP 验证码，通过后签发 Cookie
    /// </summary>
    private async Task<IActionResult> VerifyTotp(LoginPendingPayload pending)
    {
        PendingUsername = pending.Username;
        IsTotpStep = true;

        var user = await _db.Users.FindAsync(pending.UserId);
        if (user == null || user.AuthVersion != pending.AuthVersion)
        {
            ErrorMessage = "用户不存在，请重新登录";
            _pendingSessions.ClearLoginPending(HttpContext);
            IsTotpStep = false;
            return Page();
        }

        if (!_pendingSessions.BeginAttempt(HttpContext, AdminPendingSessionService.LoginCookieName, pending)
            || !await _credentials.VerifyTotpAsync(user, TotpCode))
        {
            _logger.LogWarning(
                "管理后台 TOTP 验证失败: 用户名={Username}, IP={RemoteIp}",
                user.Username, HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "验证码错误，请检查 Authenticator App 后重试";
            return Page();
        }

        if (!_pendingSessions.ConsumeLoginPending(HttpContext, pending))
            return Redirect("/admin/login");
        return await SignInUser(user!);
    }

    /// <summary>
    /// 签发认证 Cookie 并跳转到管理首页
    /// </summary>
    private async Task<IActionResult> SignInUser(Data.Entities.User user)
    {
        await _credentials.ResetFailuresAsync(user.Id);
        await _sessions.SignInAsync(HttpContext, user);

        // 首装 Key：Keep TempData 并带到 Keys 页一次性展示
        if (TempData.Peek("NewApiKey") is string)
        {
            TempData.Keep("NewApiKey");
            TempData.Keep("DeviceName");
            return Redirect("/admin/keys");
        }

        return Redirect("/admin");
    }

    /// <summary>从 TempData Peek 首把 Key，并 Keep 以便登录后仍可展示</summary>
    private void CaptureSetupApiKeyFromTempData()
    {
        SetupApiKey = TempData.Peek("NewApiKey") as string;
        SetupDeviceName = TempData.Peek("DeviceName") as string;
        if (SetupApiKey != null)
        {
            TempData.Keep("NewApiKey");
        }

        if (SetupDeviceName != null)
        {
            TempData.Keep("DeviceName");
        }
    }

    /// <summary>从加密 Cookie 恢复第二步展示状态</summary>
    private void RestoreTotpStepFromSession()
    {
        if (_pendingSessions.TryGetLoginPending(HttpContext, out var pending))
        {
            PendingUsername = pending.Username;
            IsTotpStep = true;
        }
    }
}
