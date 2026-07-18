using System.Security.Claims;
using BiSheng.Server.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BiSheng.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    /// <summary>
    /// 验证 API Key 是否有效（客户端连接测试用）
    /// </summary>
    [HttpGet("verify-key")]
    [Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
    public IActionResult VerifyKey()
    {
        return Ok(new
        {
            valid = true,
            userId = User.GetUserId().ToString(),
            username = User.FindFirstValue(ClaimTypes.Name),
            deviceName = User.GetDeviceName(),
        });
    }
}
