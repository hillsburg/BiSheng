namespace BiSheng.Shared;

/// <summary>
/// 同步协议契约：变更操作类型常量。
///
/// <para>
/// 本类定义了客户端与服务端同步协议中所有合法的变更操作类型字符串。
/// 操作类型决定同步引擎如何处理实体：
/// <list type="bullet">
///   <item><see cref="Create"/> — 远端无该实体时新建，已存在则忽略</item>
///   <item><see cref="Update"/> — 用 Payload 中的字段覆盖远端实体</item>
///   <item><see cref="Delete"/> — 软删除（IsDeleted = true）</item>
/// </list>
/// </para>
///
/// <para>
/// 合并优先级（<c>LocalChangeTracker</c> 去重规则）：
/// <c>Delete</c> 优先级最高；<c>Create</c> 后的变更仍记为 <c>Create</c>；其他情况取最新操作。
/// </para>
/// </summary>
public static class ChangeActions
{
    /// <summary>创建实体</summary>
    public const string Create = nameof(Create);

    /// <summary>更新实体（部分字段覆盖）</summary>
    public const string Update = nameof(Update);

    /// <summary>软删除实体（IsDeleted = true）</summary>
    public const string Delete = nameof(Delete);
}
