using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BiSheng.Shared;

namespace BiSheng.Server.Data.Entities;

/// <summary>
/// 同步日志：记录每个实体的变更历史，支持增量同步
/// </summary>
public class SyncLog
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(32)]
    public string EntityType { get; set; } = string.Empty; // EntityTypes.Folder | EntityTypes.Note

    [Required]
    public Guid EntityId { get; set; }

    [Required, MaxLength(16)]
    public string Action { get; set; } = string.Empty; // ChangeActions.Create | ChangeActions.Update | ChangeActions.Delete

    /// <summary>
    /// 该变更的全局单调递增版本号
    /// </summary>
    public long Version { get; set; }

    [Required]
    public Guid UserId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 变更时的实体快照（JSON），用于冲突解决
    /// </summary>
    public string? Payload { get; set; }

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
