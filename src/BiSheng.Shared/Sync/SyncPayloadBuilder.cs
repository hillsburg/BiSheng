using System;

namespace BiSheng.Shared.Sync;

/// <summary>同步 Payload 构建（JSON camelCase，供 RecordChange / SyncLog 使用）</summary>
public static class SyncPayloadBuilder
{
    public static object Folder(string name, Guid? parentId, bool isFavorite = false, bool isPinned = false)
        => new { name, parentId, isFavorite, isPinned };

    public static object Note(string title, string content, Guid folderId, bool isFavorite = false, bool isPinned = false)
        => new { title, content, folderId, isFavorite, isPinned };

    public static FolderChangePayload FolderPayload(string name, Guid? parentId, bool isFavorite, bool isPinned)
        => new() { Name = name, ParentId = parentId, IsFavorite = isFavorite, IsPinned = isPinned };

    public static NoteChangePayload NotePayload(string title, string content, Guid folderId, bool isFavorite, bool isPinned)
        => new() { Title = title, Content = content, FolderId = folderId, IsFavorite = isFavorite, IsPinned = isPinned };
}
