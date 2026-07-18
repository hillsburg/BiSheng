using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BiSheng.Server.Data.Entities;

/// <summary>
/// 笔记历史版本快照（与 SyncLog 独立；笔记软删后仍保留，需手动删除）。
/// 每笔记最多 <see cref="BiSheng.Shared.NoteRevisionLimits.MaxPerNote"/> 条，FIFO 裁剪。
/// </summary>
public class NoteRevision
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid NoteId { get; set; }

    [Required]
    public Guid UserId { get; set; }

    /// <summary>该笔记内的递增序号（1..N）</summary>
    public int RevisionNumber { get; set; }

    [Required, MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>见 <see cref="BiSheng.Shared.NoteContentHash"/></summary>
    [Required, MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>快照时 Notes.Version（全局同步版本号，便于调试）</summary>
    public long NoteVersion { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(NoteId))]
    public Note Note { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
