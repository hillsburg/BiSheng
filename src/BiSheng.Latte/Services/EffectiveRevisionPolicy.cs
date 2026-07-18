using BiSheng.Latte.Models;

namespace BiSheng.Latte.Services;

/// <summary>按数据安全档位解析笔记历史自动采样阈值</summary>
public static class EffectiveRevisionPolicy
{
    /// <summary>停笔空闲快照等待时长（分钟）</summary>
    public static int IdleSnapshotMinutes(DataSafetySettings settings) =>
        settings.Profile == DataSafetyProfile.Conservative ? 2 : LocalRevisionPolicyDefaults.IdleSnapshotMinutes;

    /// <summary>两次自动快照之间的最短间隔（分钟）</summary>
    public static int MinAutoIntervalMinutes(DataSafetySettings settings) =>
        settings.Profile == DataSafetyProfile.Conservative ? 5 : LocalRevisionPolicyDefaults.MinAutoIntervalMinutes;

    /// <summary>视为有意义改动的最小字符数变化</summary>
    public static int MinCharDelta(DataSafetySettings settings) =>
        settings.Profile == DataSafetyProfile.Conservative ? 20 : LocalRevisionPolicyDefaults.MinCharDelta;

    /// <summary>视为有意义改动的最小行数变化</summary>
    public static int MinLineDelta(DataSafetySettings settings) =>
        settings.Profile == DataSafetyProfile.Conservative ? 1 : LocalRevisionPolicyDefaults.MinLineDelta;
}

/// <summary>标准模式下的 revision 常量（与 BiSheng.Shared.LocalRevisionPolicy 对齐）</summary>
internal static class LocalRevisionPolicyDefaults
{
    /// <summary>连续无编辑超过此时长（分钟）后，尝试生成一次空闲快照</summary>
    public const int IdleSnapshotMinutes = 3;

    /// <summary>同一笔记两次自动快照之间的最短间隔（分钟）；手动/恢复不受限</summary>
    public const int MinAutoIntervalMinutes = 10;

    /// <summary>相对上一版正文字符数变化低于此值，且未满足行数阈值时，视为「微小改动」不记历史</summary>
    public const int MinCharDelta = 30;

    /// <summary>相对上一版行数变化达到此值时，视为有意义改动</summary>
    public const int MinLineDelta = 2;
}
