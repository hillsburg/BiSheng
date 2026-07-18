using System.ComponentModel.DataAnnotations;

namespace BiSheng.Latte.Data.Entities;

/// <summary>
/// 本地编辑日志（只追加）：Push 成功后打标，按保留策略裁剪
/// </summary>
public class LocalEditJournalEntry
{
    [Key]
    public long Id { get; set; }

    [Required]
    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    [Required]
    public string Action { get; set; } = string.Empty;

    /// <summary>笔记内容指纹（Update 时可选）</summary>
    [MaxLength(64)]
    public string? ContentHash { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>对应 Pending 已成功 Push 的时间；null 表示尚未上云</summary>
    public DateTime? SyncedAtUtc { get; set; }
}
