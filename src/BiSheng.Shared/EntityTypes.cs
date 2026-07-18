namespace BiSheng.Shared;

/// <summary>
/// 同步协议契约：实体类型常量。
///
/// <para>
/// 本类定义了客户端与服务端同步协议中所有合法的实体类型字符串。
/// 这些值会通过 JSON 序列化后在 HTTP 请求与 SignalR 推送中传输，
/// 因此两端的字符串必须完全一致，否则同步会静默失败（变更被跳过，数据丢失）。
/// </para>
///
/// <para>
/// 扩展说明：新增实体类型时，只需在此添加新的 <c>const string</c>，
/// 客户端和服务端通过 <c>ProjectReference</c> 自动获取，编译时即可发现未处理的分支。
/// </para>
/// </summary>
public static class EntityTypes
{
    /// <summary>文件夹实体</summary>
    public const string Folder = nameof(Folder);

    /// <summary>笔记实体</summary>
    public const string Note = nameof(Note);
}
