using System.Security.Cryptography;
using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Pages;

/// <summary>首次初始化：创建管理员 + 绑定 TOTP + 首个 API Key</summary>
public class SetupModel : PageModel
{
    private readonly AppDbContext _db;
    private readonly AdminPendingSessionService _pendingSessions;
    private readonly TotpSecretProtector _totpProtector;

    /// <summary>构造初始化页模型</summary>
    public SetupModel(
        AppDbContext db,
        AdminPendingSessionService pendingSessions,
        TotpSecretProtector totpProtector)
    {
        _db = db;
        _pendingSessions = pendingSessions;
        _totpProtector = totpProtector;
    }

    /// <summary>管理员用户名（第一步绑定）</summary>
    [BindProperty]
    public string Username { get; set; } = string.Empty;

    /// <summary>密码（第一步绑定）</summary>
    [BindProperty]
    public string Password { get; set; } = string.Empty;

    /// <summary>确认密码（第一步绑定）</summary>
    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    /// <summary>首个设备名称（第一步绑定）</summary>
    [BindProperty]
    public string DeviceName { get; set; } = string.Empty;

    /// <summary>TOTP 验证码（第二步绑定）</summary>
    [BindProperty]
    public string TotpCode { get; set; } = string.Empty;

    /// <summary>错误提示</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>二维码 data URI</summary>
    public string? QrCodeUrl { get; set; }

    /// <summary>
    /// 展示用 TOTP 密钥（仅来自服务端会话，不接受客户端回写）
    /// </summary>
    public string? DisplayTotpSecret { get; private set; }

    /// <summary>
    /// 是否处于第二步（TOTP 验证阶段）
    /// </summary>
    public bool IsVerifyStep { get; private set; }

    /// <summary>GET：已初始化则跳登录；否则若有待会话则恢复第二步</summary>
    public async Task<IActionResult> OnGetAsync()
    {
        var isSetup = await _db.ServerConfigs.AnyAsync(c => c.Id == 1 && c.IsSetup);
        if (isSetup)
        {
            return Redirect("/admin/login");
        }

        if (Request.Query.ContainsKey("reset"))
        {
            _pendingSessions.ClearSetupPending(HttpContext);
            return Redirect("/admin/setup");
        }

        RestoreVerifyStepFromSession();
        return Page();
    }

    /// <summary>POST：第一步生成密钥会话，或第二步完成初始化</summary>
    public async Task<IActionResult> OnPostAsync()
    {
        // ===== 第二步：仅信任服务端 pending Cookie 中的密钥与凭据 =====
        if (_pendingSessions.TryGetSetupPending(HttpContext, out var pending))
        {
            return await CompleteSetup(pending);
        }

        // ===== 第一步：验证表单，生成 TOTP 密钥，写入短时会话 =====
        if (string.IsNullOrWhiteSpace(Username) || Username.Length < 3)
        {
            ErrorMessage = "用户名至少 3 个字符";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Password) || Password.Length < 6)
        {
            ErrorMessage = "密码至少 6 个字符";
            return Page();
        }

        if (Password != ConfirmPassword)
        {
            ErrorMessage = "两次输入的密码不一致";
            return Page();
        }

        var totpSecret = TotpHelper.GenerateSecret();
        _pendingSessions.SetSetupPending(HttpContext, new SetupPendingState
        {
            Username = Username.Trim(),
            PasswordHash = HashPassword(Password),
            DeviceName = DeviceName?.Trim() ?? string.Empty,
            TotpSecret = totpSecret
        });

        DisplayTotpSecret = totpSecret;
        Username = Username.Trim();
        QrCodeUrl = TotpHelper.GetQrCodeDataUri(TotpHelper.GetOtpAuthUri(totpSecret, Username));
        IsVerifyStep = true;
        return Page();
    }

    /// <summary>
    /// 第二步：校验 TOTP 验证码，创建管理员，完成初始化
    /// </summary>
    private async Task<IActionResult> CompleteSetup(SetupPendingState pending)
    {
        Username = pending.Username;
        DisplayTotpSecret = pending.TotpSecret;
        IsVerifyStep = true;
        QrCodeUrl = TotpHelper.GetQrCodeDataUri(
            TotpHelper.GetOtpAuthUri(pending.TotpSecret, pending.Username));

        if (!TotpHelper.VerifyCode(pending.TotpSecret, TotpCode))
        {
            ErrorMessage = "验证码错误，请检查 Authenticator App 后重试";
            return Page();
        }

        // 创建管理员（密码已在第一步哈希；TOTP 密钥加密落库）
        var user = new User
        {
            Username = pending.Username,
            PasswordHash = pending.PasswordHash,
            TotpSecret = _totpProtector.Protect(pending.TotpSecret)
        };
        _db.Users.Add(user);

        // 生成首个 API Key
        var apiKeyValue = GenerateApiKey();
        var apiKey = new ApiKey
        {
            KeyValue = ApiKeyAuthHandler.HashApiKey(apiKeyValue),
            DeviceName = string.IsNullOrWhiteSpace(pending.DeviceName) ? "Default Device" : pending.DeviceName,
            UserId = user.Id,
            IsActive = true
        };
        _db.ApiKeys.Add(apiKey);

        // 标记已设置
        _db.ServerConfigs.Add(new ServerConfig { Id = 1, IsSetup = true, SetupAt = DateTime.UtcNow });

        await _db.SaveChangesAsync();
        _pendingSessions.ClearSetupPending(HttpContext);

        // 跳转到登录页（携带 setup 标记）
        TempData["NewApiKey"] = apiKeyValue;
        TempData["DeviceName"] = apiKey.DeviceName;
        return Redirect("/admin/login?setup=done");
    }

    /// <summary>从内存会话恢复第二步展示状态</summary>
    private void RestoreVerifyStepFromSession()
    {
        if (!_pendingSessions.TryGetSetupPending(HttpContext, out var pending))
        {
            return;
        }

        Username = pending.Username;
        DisplayTotpSecret = pending.TotpSecret;
        QrCodeUrl = TotpHelper.GetQrCodeDataUri(
            TotpHelper.GetOtpAuthUri(pending.TotpSecret, pending.Username));
        IsVerifyStep = true;
    }

    /// <summary>生成 32 字节随机 API Key（hex 小写）</summary>
    internal static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// 使用 PBKDF2（ASP.NET Identity 内置 PasswordHasher）哈希密码
    /// </summary>
    internal static string HashPassword(string password)
    {
        return PasswordHasher.HashPassword(null!, password);
    }

    /// <summary>
    /// 存储哈希是否为当前 Identity PasswordHasher 可解析的 Base64 格式。
    /// 旧库或手工种子数据可能不是此格式，直接 Verify 会抛 FormatException。
    /// </summary>
    internal static bool IsIdentityPasswordHash(string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        try
        {
            _ = Convert.FromBase64String(storedHash);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 验证密码（PBKDF2）。哈希为空、格式非法或校验失败时返回 false，不向外抛异常。
    /// </summary>
    internal static bool VerifyPassword(string password, string? storedHash)
    {
        if (string.IsNullOrEmpty(password) || !IsIdentityPasswordHash(storedHash))
        {
            return false;
        }

        try
        {
            var result = PasswordHasher.VerifyHashedPassword(null!, storedHash!, password);
            return result == PasswordVerificationResult.Success
                || result == PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // 双重保险：极端情况下 Identity 仍可能抛格式异常
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static readonly PasswordHasher<object> PasswordHasher = new();
}
