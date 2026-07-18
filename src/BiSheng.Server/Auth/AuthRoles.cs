namespace BiSheng.Server.Auth;

/// <summary>授权角色名（与 Cookie 登录时写入的 <see cref="System.Security.Claims.ClaimTypes.Role"/> 一致）</summary>
public static class AuthRoles
{
    /// <summary>管理面板管理员（Setup 创建的唯一 Web 用户）</summary>
    public const string Admin = "Admin";
}
