using System.Security.Claims;
using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
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

    /// <summary>构造登录页模型</summary>
    public LoginModel(
        AppDbContext db,
        IWebHostEnvironment env,
        AdminPendingSessionService pendingSessions,
        TotpSecretProtector totpProtector,
        ILogger<LoginModel> logger)
    {
        _db = db;
        _env = env;
        _pendingSessions = pendingSessions;
        _totpProtector = totpProtector;
        _logger = logger;
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
            return Redirect("/admin");
        }

        // 用户点击「返回登录」时清除待会话
        if (Request.Query.ContainsKey("reset"))
        {
            _pendingSessions.ClearLoginPending(HttpContext);
            return Redirect("/admin/login");
        }

        RestoreTotpStepFromSession();
        return Page();
    }

    /// <summary>POST：第一步验密或第二步验 TOTP</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        // ===== 第二步：仅信任服务端 pending Cookie，忽略客户端伪造的 userId =====
        if (_pendingSessions.TryGetLoginPending(HttpContext, out var pending))
        {
            return await VerifyTotp(pending);
        }

        // ===== 第一步：校验用户名 + 密码 =====
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == Username);
        if (user == null)
        {
            _logger.LogWarning(
                "管理后台登录失败: 用户名={Username}, IP={RemoteIp}",
                Username, HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "用户名或密码错误";
            return Page();
        }

        // 旧库密码哈希可能不是 Identity Base64 格式，避免 Verify 抛 500
        if (!SetupModel.IsIdentityPasswordHash(user.PasswordHash))
        {
            _logger.LogError(
                "管理后台登录中止: 用户 {Username} 的 PasswordHash 无法被当前版本解析（可能来自旧数据库）",
                user.Username);
            ErrorMessage =
                "该账户的密码存储格式与当前版本不兼容（常见于沿用旧版数据库）。"
                + "请删除服务端数据库后重新完成初始化向导，或重置管理员密码后再登录。";
            return Page();
        }

        if (!SetupModel.VerifyPassword(Password, user.PasswordHash))
        {
            _logger.LogWarning(
                "管理后台登录失败: 用户名={Username}, IP={RemoteIp}",
                Username, HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "用户名或密码错误";
            return Page();
        }

        // 开发调试环境：跳过 TOTP，直接登录
        if (_env.IsDevelopment())
        {
            return await SignInUser(user);
        }

        // 生产 / Test：密码验证通过，写入短时会话后进入 TOTP 步骤
        var plainTotp = _totpProtector.Unprotect(user.TotpSecret);
        if (string.IsNullOrWhiteSpace(plainTotp))
        {
            _logger.LogError(
                "用户 {Username} 的 TotpSecret 为空或无法解密，无法完成生产登录",
                user.Username);
            ErrorMessage = "该账户尚未绑定两步验证，请重新完成初始化或联系管理员配置 TOTP。";
            return Page();
        }

        // 明文存量：登录前升级为密文
        if (!TotpSecretProtector.IsProtected(user.TotpSecret))
        {
            user.TotpSecret = _totpProtector.Protect(plainTotp);
            await _db.SaveChangesAsync();
        }

        _pendingSessions.SetLoginPending(HttpContext, user.Id, user.Username);
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
        if (user == null)
        {
            ErrorMessage = "用户不存在，请重新登录";
            _pendingSessions.ClearLoginPending(HttpContext);
            IsTotpStep = false;
            return Page();
        }

        var plainTotp = _totpProtector.Unprotect(user.TotpSecret);
        if (!TotpHelper.VerifyCode(plainTotp, TotpCode))
        {
            _logger.LogWarning(
                "管理后台 TOTP 验证失败: 用户名={Username}, IP={RemoteIp}",
                user.Username, HttpContext.Connection.RemoteIpAddress);
            ErrorMessage = "验证码错误，请检查 Authenticator App 后重试";
            return Page();
        }

        if (!TotpSecretProtector.IsProtected(user.TotpSecret) && !string.IsNullOrWhiteSpace(plainTotp))
        {
            user.TotpSecret = _totpProtector.Protect(plainTotp);
            await _db.SaveChangesAsync();
        }

        _pendingSessions.ClearLoginPending(HttpContext);
        return await SignInUser(user);
    }

    /// <summary>
    /// 签发认证 Cookie 并跳转到管理首页
    /// </summary>
    private async Task<IActionResult> SignInUser(Data.Entities.User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Role, AuthRoles.Admin),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

        return Redirect("/admin");
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
