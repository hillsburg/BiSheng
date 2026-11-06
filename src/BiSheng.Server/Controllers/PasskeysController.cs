using System.ComponentModel.DataAnnotations;
using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Fido2NetLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Controllers;

/// <summary>后台专用通行密钥接口；所有写操作均校验防伪令牌和限流。</summary>
[ApiController]
[Route("api/admin/passkeys")]
[AutoValidateAntiforgeryToken]
[EnableRateLimiting("login")]
[RequestSizeLimit(262144)]
public sealed class PasskeysController(AppDbContext db, PasskeyService passkeys, AdminCredentialService credentials,
    IWebHostEnvironment environment, ILogger<PasskeysController> logger) : Controller
{
    private const string ReauthenticationFailed = "密码或验证码错误、验证码已使用，或账户暂时锁定。";

    /// <summary>功能关闭时隐藏接口；只把预期的拒绝转换为 400，其余异常照常暴露为服务端错误。</summary>
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!passkeys.Enabled)
        {
            context.Result = NotFound();
            return;
        }
        var executed = await next();
        if (executed.Exception is PasskeyRejectedException rejected)
        {
            logger.LogWarning("通行密钥操作被拒绝：{Reason}（{Cause}）", rejected.Message, rejected.InnerException?.GetType().Name);
            executed.ExceptionHandled = true;
            executed.Result = BadRequest(new { message = rejected.Message });
        }
    }

    /// <summary>匿名获取登录挑战，不泄露账号及已绑定凭据信息。</summary>
    [HttpPost("login/options")]
    public IActionResult LoginOptions() => Content(passkeys.BeginLogin(HttpContext), "application/json");

    /// <summary>签名验证成功后签发与密码登录相同的后台 Cookie。</summary>
    [HttpPost("login/complete")]
    public async Task<IActionResult> CompleteLogin([FromBody] AuthenticatorAssertionRawResponse response)
    {
        await passkeys.FinishLoginAsync(HttpContext, response);
        return Ok(new { redirect = "/admin" });
    }

    /// <summary>列出当前账号绑定的通行密钥。</summary>
    [AdminPanelAuthorize]
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await passkeys.ListAsync(User.GetUserId()));

    /// <summary>验证当前凭据后发起绑定，授权被固定在服务端 challenge 状态中。</summary>
    [AdminPanelAuthorize]
    [HttpPost("register/options")]
    public async Task<IActionResult> RegistrationOptions([FromBody] RegisterPasskeyRequest request)
    {
        var user = await ReauthenticateAsync(request);
        if (user == null)
        {
            return BadRequest(new { message = ReauthenticationFailed });
        }
        return Content(await passkeys.BeginRegistrationAsync(HttpContext, user, request.Name.Trim(), request.UseOtherDevice), "application/json");
    }

    /// <summary>只允许当前 Cookie 所属账号完成之前已授权的绑定。</summary>
    [AdminPanelAuthorize]
    [HttpPost("register/complete")]
    public async Task<IActionResult> CompleteRegistration([FromBody] AuthenticatorAttestationRawResponse response)
    {
        var user = await db.Users.SingleAsync(u => u.Id == User.GetUserId());
        await passkeys.FinishRegistrationAsync(HttpContext, user, response);
        return Ok(new { success = true });
    }

    /// <summary>重命名只校验归属，名称只作为文本显示。</summary>
    [AdminPanelAuthorize]
    [HttpPost("{id:guid}/rename")]
    public async Task<IActionResult> Rename(Guid id, [FromBody] RenamePasskeyRequest request) =>
        await passkeys.RenameAsync(User.GetUserId(), id, request.Name.Trim()) ? Ok(new { success = true }) : NotFound();

    /// <summary>删除前要求重新授权，并撤销其他后台会话。</summary>
    [AdminPanelAuthorize]
    [HttpPost("{id:guid}/delete")]
    public async Task<IActionResult> Delete(Guid id, [FromBody] ReauthenticateRequest request)
    {
        var user = await ReauthenticateAsync(request);
        if (user == null)
        {
            return BadRequest(new { message = ReauthenticationFailed });
        }
        return await passkeys.DeleteAsync(HttpContext, user, id) ? Ok(new { success = true }) : NotFound();
    }

    /// <summary>复用阶段一授权：当前密码加近期验证，必要时要求新的 TOTP。</summary>
    private async Task<User?> ReauthenticateAsync(ReauthenticateRequest request)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == User.GetUserId());
        if (user == null || !await credentials.AuthorizeSensitiveOperationAsync(HttpContext, user,
            request.Password, request.TotpCode, environment.IsDevelopment()))
        {
            return null;
        }
        return user;
    }
}

/// <summary>敏感操作的重新授权输入，密码和验证码不得写入日志。</summary>
public class ReauthenticateRequest
{
    [Required, StringLength(1024)]
    public string Password { get; set; } = string.Empty;
    [StringLength(6)]
    public string TotpCode { get; set; } = string.Empty;
}

/// <summary>绑定新通行密钥：名称加重新授权。</summary>
public sealed class RegisterPasskeyRequest : ReauthenticateRequest
{
    /// <summary>显式选择手机或安全密钥；默认使用当前设备的内置认证器。</summary>
    public bool UseOtherDevice { get; set; }

    [Required, StringLength(64), RegularExpression(@".*\S.*", ErrorMessage = "名称不能为空。")]
    public string Name { get; set; } = string.Empty;
}

/// <summary>重命名只需要新名称。</summary>
public sealed class RenamePasskeyRequest
{
    [Required, StringLength(64), RegularExpression(@".*\S.*", ErrorMessage = "名称不能为空。")]
    public string Name { get; set; } = string.Empty;
}
