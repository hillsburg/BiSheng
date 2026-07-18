namespace BiSheng.Shared;

/// <summary>
/// 笔记历史版本的自动快照策略常量（客户端闲置采样与服务端 Push 附属写历史共用）。
/// </summary>
public static class LocalRevisionPolicy
{
    /// <summary>连续无编辑超过此时长（分钟）后，尝试生成一次空闲快照</summary>
    public const int IdleSnapshotMinutes = 3;

    /// <summary>同一笔记两次自动快照之间的最短间隔（分钟）；手动/恢复可不受限</summary>
    public const int MinAutoIntervalMinutes = 10;

    /// <summary>
    /// 相对上一版正文字符数变化低于此值，且未满足行数阈值时，视为「微小改动」不记历史
    /// </summary>
    public const int MinCharDelta = 30;

    /// <summary>相对上一版行数变化达到此值时，视为有意义改动</summary>
    public const int MinLineDelta = 2;
}
