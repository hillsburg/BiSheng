using System;

namespace BiSheng.Shared.Sync;

/// <summary>历史版本列表项（不含正文，用于列表展示）</summary>
public class NoteRevisionListItemDto
{
    public Guid Id { get; set; }
    public Guid NoteId { get; set; }

    /// <summary>该笔记内的递增序号</summary>
    public int RevisionNumber { get; set; }

    public string Title { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>快照时服务端 Notes.Version（调试用，本地历史可为 0）</summary>
    public long NoteVersion { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>历史版本详情（含正文）</summary>
public class NoteRevisionDto : NoteRevisionListItemDto
{
    public string Content { get; set; } = string.Empty;
}

/// <summary>恢复历史版本后服务端返回的笔记摘要</summary>
public class NoteRestoreResultDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public Guid FolderId { get; set; }
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }
}
