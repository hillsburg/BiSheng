using System.Security.Claims;
using BiSheng.Server.Auth;
using BiSheng.Server.Services;
using BiSheng.Shared.Compatibility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly CompatibilityOptions _compatibility;

    /// <summary>构造认证控制器</summary>
    public AuthController(IOptions<CompatibilityOptions> compatibility)
    {
        _compatibility = compatibility.Value;
    }

    /// <summary>
    /// 验证 API Key，并返回协议兼容信息（客户端连接测试用）
    /// </summary>
    [HttpGet("verify-key")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
    public ActionResult<VerifyKeyResponse> VerifyKey()
    {
        var serverVersion = ServerUpdateCheckService.GetCurrentVersion();
        var minClient = _compatibility.EffectiveMinClient;
        Request.Headers.TryGetValue(ProtocolCompatibility.ClientVersionHeaderName, out var clientVersionValues);
        var clientVersion = clientVersionValues.ToString();
        if (string.IsNullOrWhiteSpace(clientVersion))
        {
            clientVersion = null;
        }

        var compatible = true;
        string? message = null;
        if (!string.IsNullOrWhiteSpace(clientVersion)
            && !VersionComparer.IsAtLeast(clientVersion, minClient))
        {
            compatible = false;
            message =
                $"客户端版本过旧（当前 {clientVersion}，需要 ≥ {minClient}）。请升级 BiSheng Latte。";
        }

        return Ok(new VerifyKeyResponse
        {
            Valid = true,
            Compatible = compatible,
            UserId = User.GetUserId().ToString(),
            Username = User.FindFirstValue(ClaimTypes.Name),
            DeviceName = User.GetDeviceName(),
            ServerVersion = serverVersion,
            MinClient = minClient,
            ClientVersion = clientVersion,
            CompatibilityMessage = message
        });
    }
}
