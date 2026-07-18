using System.Security.Claims;

namespace BiSheng.Server.Auth;

/// <summary>从 API Key 认证的 ClaimsPrincipal 读取常用字段</summary>
public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);

    public static Guid GetApiKeyId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(BiShengClaimTypes.ApiKeyId)!);

    public static string? GetDeviceName(this ClaimsPrincipal user) =>
        user.FindFirstValue(BiShengClaimTypes.DeviceName);
}
