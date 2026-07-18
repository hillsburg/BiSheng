using System.ComponentModel.DataAnnotations;

namespace BiSheng.Latte.Data.Entities;

/// <summary>
/// 本地笔记历史版本（离线编辑时按采样策略记录；Push 成功后服务端另有独立快照）。
/// </summary>
public class LocalNoteRevision
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid NoteId { get; set; }

    /// <summary>该笔记内的递增序号</summary>
    public int RevisionNumber { get; set; }

    [MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>是否已对应到服务端历史（预留，当前未同步回填）</summary>
    public bool SyncedToServer { get; set; }

    public Guid? ServerRevisionId { get; set; }
}
