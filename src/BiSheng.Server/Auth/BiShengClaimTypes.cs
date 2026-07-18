namespace BiSheng.Server.Auth;

/// <summary>
/// API Key 认证签发的自定义 Claim 类型（与 <see cref="ApiKeyAuthHandler"/> 一致）
/// </summary>
public static class BiShengClaimTypes
{
    /// <summary>当前请求对应的 ApiKeys.Id</summary>
    public const string ApiKeyId = "bisheng:api_key_id";

    /// <summary>ApiKeys.DeviceName</summary>
    public const string DeviceName = "bisheng:device_name";
}
