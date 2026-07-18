using System.ComponentModel.DataAnnotations;
using BiSheng.Shared;

namespace BiSheng.Latte.Data.Entities;

/// <summary>
/// 本地待推送变更记录（独立表，替代原 LocalSyncState.PendingChanges JSON 单行存储）
/// 
/// 设计改进：
/// - 去重合并：同一实体的多次变更只保留最新一条
/// - 避免 JSON 反序列化/序列化瓶颈
/// - 高频编辑时只写入单行，无需读写整个队列
/// </summary>
public class LocalPendingChange
{
    [Key]
    public int Id { get; set; }

    /// <summary>实体类型：<see cref="EntityTypes.Folder"/> 或 <see cref="EntityTypes.Note"/></summary>
    [Required, StringLength(32)]
    public string EntityType { get; set; } = string.Empty;

    /// <summary>实体唯一标识</summary>
    [Required]
    public Guid EntityId { get; set; }

    /// <summary>操作类型：<see cref="ChangeActions.Create"/> / <see cref="ChangeActions.Update"/> / <see cref="ChangeActions.Delete"/></summary>
    [Required, StringLength(16)]
    public string Action { get; set; } = string.Empty;

    /// <summary>变更内容的 JSON 序列化</summary>
    public string? Payload { get; set; }

    /// <summary>本地操作的时间戳（用于冲突解决）</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
