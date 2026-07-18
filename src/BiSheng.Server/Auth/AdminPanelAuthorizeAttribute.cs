using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace BiSheng.Server.Auth;

/// <summary>
/// 管理面板仅允许 Cookie 登录的管理员访问，拒绝 API Key。
/// 等价于 <c>[Authorize(AuthenticationSchemes = Cookie, Roles = AuthRoles.Admin)]</c>
/// </summary>
public sealed class AdminPanelAuthorizeAttribute : AuthorizeAttribute
{
    public AdminPanelAuthorizeAttribute()
    {
        AuthenticationSchemes = CookieAuthenticationDefaults.AuthenticationScheme;
        Roles = AuthRoles.Admin;
    }
}
