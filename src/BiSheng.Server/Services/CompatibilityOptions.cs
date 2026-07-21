using BiSheng.Shared.Compatibility;

namespace BiSheng.Server.Services;

/// <summary>协议兼容门槛（服务端侧，可覆盖 Shared 默认值）</summary>
public sealed class CompatibilityOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "Compatibility";

    /// <summary>要求客户端不低于此版本；空则使用 Shared 默认</summary>
    public string? MinClient { get; set; }

    /// <summary>解析后的有效 MinClient</summary>
    public string EffectiveMinClient =>
        string.IsNullOrWhiteSpace(MinClient)
            ? ProtocolCompatibility.DefaultMinClient
            : MinClient.Trim();
}
