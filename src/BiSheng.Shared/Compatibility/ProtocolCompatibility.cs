namespace BiSheng.Shared.Compatibility;

/// <summary>
/// 客户端 / 服务端协议兼容门槛。
/// 破坏性同步协议变更时抬高对应常量，并随产品版本一起发版。
/// </summary>
public static class ProtocolCompatibility
{
    /// <summary>客户端在请求中声明自身版本的 Header</summary>
    public const string ClientVersionHeaderName = "X-BiSheng-Client-Version";

    /// <summary>
    /// 服务端默认要求的最低客户端版本（可被 appsettings Compatibility:MinClient 覆盖）。
    /// 当前无破坏性门槛，设为 0.0.0 表示任意客户端均可。
    /// </summary>
    public const string DefaultMinClient = "0.0.0";

    /// <summary>
    /// 客户端要求的最低服务端版本。
    /// 破坏协议时抬高，旧服务端在「测试连接」阶段会被拒绝。
    /// </summary>
    public const string MinServerRequiredByClient = "0.0.0";
}
