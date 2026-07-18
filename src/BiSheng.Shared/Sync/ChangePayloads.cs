using System;

namespace BiSheng.Shared.Sync;

/// <summary>文件夹同步 Payload 结构（JSON camelCase：name, parentId, isFavorite, isPinned）</summary>
public record FolderChangePayload
{
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsPinned { get; init; }
}

/// <summary>笔记同步 Payload 结构（JSON camelCase：title, content, folderId, isFavorite, isPinned）</summary>
public record NoteChangePayload
{
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public Guid FolderId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsPinned { get; init; }
}
