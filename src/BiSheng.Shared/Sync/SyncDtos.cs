using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BiSheng.Shared.Sync;

/// <summary>客户端推送变更请求</summary>
public record SyncPushRequest
{
    /// <summary>客户端最后一次同步的版本号</summary>
    public long ClientVersion { get; init; }

    /// <summary>待推送的变更列表</summary>
    [Required]
    public List<ClientChangeDto> Changes { get; init; } = new();
}

/// <summary>客户端发起的单条变更</summary>
public record ClientChangeDto
{
    [Required, StringLength(32)]
    public string EntityType { get; init; } = string.Empty;

    [Required]
    public Guid EntityId { get; init; }

    [Required, StringLength(16)]
    public string Action { get; init; } = string.Empty;

    /// <summary>变更后的实体数据（JSON）</summary>
    public string? Payload { get; init; }

    public DateTime? UpdatedAt { get; init; }
}

/// <summary>服务端返回的拉取结果</summary>
public record SyncPullResponse
{
    /// <summary>本批变更（终态折叠后）</summary>
    public List<ChangeDto> Changes { get; init; } = new();

    /// <summary>服务端当前水位（tip），用于判断是否落后；最后一批客户端应推进到此值</summary>
    public long ServerVersion { get; init; }

    /// <summary>
    /// 常规增量：本批已消费到的 SyncLog 版本，下次 since 用此值。
    /// 实体快照（IsEntitySnapshot）：有后续页时为下一页 snapshotOffset；末页等于 ServerVersion。
    /// </summary>
    public long NextSince { get; init; }

    /// <summary>是否还有后续批次</summary>
    public bool HasMore { get; init; }

    /// <summary>
    /// 客户端 since 低于 SyncLog 裁剪线，需清空本地后以 since=0 拉实体快照重建。
    /// since=0 的快照请求本身不会置此标志。
    /// </summary>
    public bool RequiresFullSync { get; init; }

    /// <summary>本批是否为 since=0 实体快照页（非 SyncLog 增量）</summary>
    public bool IsEntitySnapshot { get; init; }
}

/// <summary>服务端变更条目（Pull / SignalR 共用）</summary>
public record ChangeDto
{
    public string EntityType { get; init; } = string.Empty;
    public Guid EntityId { get; init; }
    public string Action { get; init; } = string.Empty;
    public long Version { get; init; }
    public DateTime Timestamp { get; init; }
    public string? Payload { get; init; }
}

/// <summary>Push 响应</summary>
public record SyncPushResponse
{
    public bool Success { get; init; }
    public long ServerVersion { get; init; }
    public List<string> Errors { get; init; } = new();
    public List<ChangeDto> ConflictingChanges { get; init; } = new();
    public List<Guid> FailedEntityIds { get; init; } = new();

    /// <summary>
    /// E：本批 Push 覆盖的远端 pre-state。
    /// 客户端推送实体 X 的 Update 成功，同时区间 [ClientVersion+1, 推送前服务端水位] 内
    /// 有其他设备对 X 的变更——这些变更被本批静默覆盖。服务端在此返回它们供客户端
    /// 检测是否构成真实冲突（内容不同则建 SyncConflict 提示用户，不回滚本地）
    /// </summary>
    public List<ChangeDto> OverwrittenChanges { get; init; } = new();

    /// <summary>客户端版本低于服务端裁剪线，需全量重建后再 Push</summary>
    public bool RequiresFullSync { get; init; }

    /// <summary>事务整体回滚，客户端应保留全部 pending 并重试；ServerVersion 为回滚后的真实水位</summary>
    public bool TransactionRolledBack { get; init; }
}
