namespace BiSheng.Server.Services.Mutations;

/// <summary>与 ClientChangeDto / REST 等价的内部变更描述</summary>
public sealed record EntityMutation
{
    /// <summary>实体类型：Note / Folder</summary>
    public required string EntityType { get; init; }

    /// <summary>实体 ID</summary>
    public required Guid EntityId { get; init; }

    /// <summary>变更动作：Create / Update / Delete</summary>
    public required string Action { get; init; }

    /// <summary>JSON payload；Delete 可为 null</summary>
    public string? Payload { get; init; }

    /// <summary>客户端或 REST 声明的更新时间</summary>
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>级联产生的子变更（如删 Folder 的子孙）</summary>
public sealed record CascadeAppliedMutation(
    string EntityType,
    Guid EntityId,
    long Version,
    DateTime UpdatedAt);

/// <summary>单条变更成功后的结果（供 Push 批次汇总 / REST 返回 / Notify）</summary>
public sealed record AppliedMutation
{
    /// <summary>根变更</summary>
    public required EntityMutation Mutation { get; init; }

    /// <summary>分配的版本号</summary>
    public required long Version { get; init; }

    /// <summary>写入 SyncLog 用的 payload（Delete 可能为 null）</summary>
    public string? Payload { get; init; }

    /// <summary>级联产生的子变更，每条独立版本 + SyncLog</summary>
    public IReadOnlyList<CascadeAppliedMutation> Cascaded { get; init; }
        = Array.Empty<CascadeAppliedMutation>();
}

/// <summary>Push 批次内共享的 Writer 上下文</summary>
public sealed class MutationBatchContext
{
    /// <summary>本批 Push 中已成功创建/更新的 folder Id，供 FK 校验</summary>
    public HashSet<Guid> AvailableFolderIds { get; init; } = new();
}

/// <summary>Writer 调用选项</summary>
public sealed class MutationWriteOptions
{
    /// <summary>是否在 Note 变更时写 Revision</summary>
    public bool RecordNoteRevision { get; init; } = true;

    /// <summary>
    /// 强制写历史（仅 hash 去重）：用于 REST「恢复历史版本」，不受 Push 自动采样间隔约束
    /// </summary>
    public bool ForceNoteRevision { get; init; }
}
