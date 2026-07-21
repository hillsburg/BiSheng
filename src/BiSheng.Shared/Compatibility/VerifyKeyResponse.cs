using System.Text.Json.Serialization;

namespace BiSheng.Shared.Compatibility;

/// <summary>GET /api/auth/verify-key 响应（连接探测与协议兼容）</summary>
public sealed class VerifyKeyResponse
{
    [JsonPropertyName("valid")]
    public bool Valid { get; set; }

    /// <summary>API Key 有效且客户端/服务端版本互相满足门槛</summary>
    [JsonPropertyName("compatible")]
    public bool Compatible { get; set; } = true;

    [JsonPropertyName("userId")]
    public string? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    /// <summary>服务端产品版本</summary>
    [JsonPropertyName("serverVersion")]
    public string? ServerVersion { get; set; }

    /// <summary>服务端要求的最低客户端版本</summary>
    [JsonPropertyName("minClient")]
    public string? MinClient { get; set; }

    /// <summary>请求中声明的客户端版本（若有）</summary>
    [JsonPropertyName("clientVersion")]
    public string? ClientVersion { get; set; }

    /// <summary>不兼容时的说明</summary>
    [JsonPropertyName("compatibilityMessage")]
    public string? CompatibilityMessage { get; set; }
}
