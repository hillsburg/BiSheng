using System.Text.Json;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services.Sync;

/// <summary>
/// 远端变更合并：冲突检测、payload 应用与级联删除
/// </summary>
internal static class SyncRemoteChangeMerger
{
    /// <summary>
    /// 合并远端变更：有待推送且与远端终态不一致才记冲突；一致则视为 Push 回声并清除 pending
    /// </summary>
    /// <returns>是否新建了冲突记录</returns>
    internal static bool MergeRemoteChange(LocalDbContext db, ChangeDto change)
    {
        var conflictingLocal = db.PendingChanges
            .FirstOrDefault(p => p.EntityType == change.EntityType && p.EntityId == change.EntityId);

        if (conflictingLocal != null)
        {
            if (IsGenuineConflict(conflictingLocal, change))
            {
                var created = RecordConflictFromRemote(db, change, conflictingLocal);
                SyncPendingOrdering.RemovePendingChange(db, conflictingLocal);
                return created;
            }

            SyncPendingOrdering.RemovePendingChange(db, conflictingLocal);
        }

        ApplyRemoteChangeToLocalDb(db, change);
        return false;
    }

    /// <summary>
    /// 判断本地待推送与远端变更是否构成真实冲突（内容不一致或删改方向冲突）
    /// </summary>
    internal static bool IsGenuineConflict(LocalPendingChange localPending, ChangeDto remote)
    {
        if (localPending.Action == ChangeActions.Delete && remote.Action == ChangeActions.Delete)
        {
            return false;
        }

        if (localPending.Action == ChangeActions.Delete || remote.Action == ChangeActions.Delete)
        {
            return true;
        }

        if (string.IsNullOrEmpty(localPending.Payload) || string.IsNullOrEmpty(remote.Payload))
        {
            return !string.Equals(localPending.Payload, remote.Payload, StringComparison.Ordinal);
        }

        return !SyncPayloadFingerprint.AreEquivalent(localPending.EntityType, localPending.Payload, remote.Payload);
    }

    /// <summary>从冲突/远端 payload 写入笔记字段</summary>
    internal static void ApplyRemoteNotePayload(
        LocalDbContext db,
        Guid entityId,
        string? remotePayload,
        DateTime remoteUpdatedAt)
    {
        var note = db.Notes.Find(entityId);
        if (note == null || string.IsNullOrEmpty(remotePayload))
        {
            return;
        }

        try
        {
            var root = JsonDocument.Parse(remotePayload).RootElement;
            note.Title = SyncPayloadReader.ReadString(root, "title", note.Title);
            note.Content = SyncPayloadReader.ReadString(root, "content", note.Content);
            note.FolderId = SyncPayloadReader.ReadGuid(root, "folderId", note.FolderId);
            note.IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite", note.IsFavorite);
            note.IsPinned = SyncPayloadReader.ReadBool(root, "isPinned", note.IsPinned);
        }
        catch
        {
            note.Content = remotePayload;
        }

        note.UpdatedAt = remoteUpdatedAt;
    }

    /// <summary>从冲突/远端 payload 写入文件夹字段</summary>
    internal static void ApplyRemoteFolderPayload(
        LocalDbContext db,
        Guid entityId,
        string? remotePayload,
        DateTime remoteUpdatedAt)
    {
        var folder = db.Folders.Find(entityId);
        if (folder == null || string.IsNullOrEmpty(remotePayload))
        {
            return;
        }

        try
        {
            var root = JsonDocument.Parse(remotePayload).RootElement;
            folder.Name = SyncPayloadReader.ReadString(root, "name", folder.Name);
            folder.ParentId = SyncPayloadReader.ReadNullableGuid(root, "parentId", folder.ParentId);
            folder.IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite", folder.IsFavorite);
            folder.IsPinned = SyncPayloadReader.ReadBool(root, "isPinned", folder.IsPinned);
        }
        catch
        {
            folder.Name = remotePayload;
        }

        folder.UpdatedAt = remoteUpdatedAt;
    }

    /// <summary>将冲突记录到 SyncConflict 表</summary>
    /// <returns>是否新建了冲突记录</returns>
    internal static bool RecordConflictFromRemote(LocalDbContext db, ChangeDto change, LocalPendingChange localPending)
    {
        string entityType = change.EntityType;
        Guid entityId = change.EntityId;
        string? remotePayload = change.Payload;
        DateTime remoteTimestamp = change.Timestamp;

        // 读取本地版本内容（展示用）与标题
        string localContent = string.Empty;
        string entityTitle = string.Empty;

        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            if (note != null)
            {
                localContent = note.IsDeleted ? "（已删除）" : note.Content;
                entityTitle = note.Title;
            }
            else if (localPending.Action == ChangeActions.Delete)
            {
                localContent = "（已删除）";
            }
        }
        else if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            if (folder != null)
            {
                localContent = folder.IsDeleted ? "（已删除）" : folder.Name;
                entityTitle = folder.Name;
            }
            else if (localPending.Action == ChangeActions.Delete)
            {
                localContent = "（已删除）";
            }
        }

        // 远端展示内容：Delete 用占位，否则从 payload 提取
        string remoteContent = change.Action == ChangeActions.Delete
            ? "（已删除）"
            : ExtractDisplayContent(entityType, remotePayload, ref entityTitle);

        if (db.SyncConflicts.Any(c =>
                !c.IsResolved && c.EntityType == entityType && c.EntityId == entityId))
        {
            return false;
        }

        var conflict = new SyncConflict
        {
            EntityType = entityType,
            EntityId = entityId,
            EntityTitle = entityTitle,
            LocalContent = localContent,
            RemoteContent = remoteContent,
            LocalAction = localPending.Action,
            RemoteAction = change.Action,
            LocalPayload = localPending.Payload,
            RemotePayload = remotePayload,
            LocalUpdatedAt = localPending.UpdatedAt,
            RemoteUpdatedAt = remoteTimestamp
        };

        db.SyncConflicts.Add(conflict);
        return true;
    }

    /// <summary>从完整 payload 提取对话框展示用正文/名称</summary>
    private static string ExtractDisplayContent(string entityType, string? payload, ref string entityTitle)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        try
        {
            var root = JsonDocument.Parse(payload).RootElement;
            if (entityType == EntityTypes.Note)
            {
                if (string.IsNullOrEmpty(entityTitle))
                {
                    entityTitle = SyncPayloadReader.ReadString(root, "title", entityTitle);
                }

                return SyncPayloadReader.ReadString(root, "content", payload);
            }

            if (entityType == EntityTypes.Folder)
            {
                var name = SyncPayloadReader.ReadString(root, "name", payload);
                if (string.IsNullOrEmpty(entityTitle))
                {
                    entityTitle = name;
                }

                return name;
            }
        }
        catch
        {
            // payload 解析失败，保留原始 JSON
        }

        return payload;
    }

    /// <summary>
    /// E：处理本批 Push 覆盖的远端 pre-state。
    /// pending 已清、本地实体已是客户端版本（服务端接受），不回滚本地；
    /// 仅当本地内容与远端 pre-state 不同时建 SyncConflict 提示用户"我覆盖了其他设备的编辑"
    /// </summary>
    /// <returns>是否新建了冲突记录</returns>
    internal static bool RecordConflictFromOverwritten(LocalDbContext db, ChangeDto change)
    {
        string entityType = change.EntityType;
        Guid entityId = change.EntityId;

        // 已有未解决冲突则不重复记录
        if (db.SyncConflicts.Any(c =>
                !c.IsResolved && c.EntityType == entityType && c.EntityId == entityId))
        {
            return false;
        }

        // 读取本地实体当前内容 + UpdatedAt（即客户端刚推送的版本）
        string localContent = string.Empty;
        string entityTitle = string.Empty;
        DateTime localUpdatedAt = DateTime.UtcNow;

        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            if (note == null)
            {
                return false;
            }
            localContent = note.Content;
            entityTitle = note.Title;
            localUpdatedAt = note.UpdatedAt;
        }
        else if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            if (folder == null)
            {
                return false;
            }
            localContent = folder.Name;
            entityTitle = folder.Name;
            localUpdatedAt = folder.UpdatedAt;
        }
        else
        {
            return false;
        }

        // 提取远端 pre-state 展示内容；完整 payload 另存
        var remoteContent = change.Action == ChangeActions.Delete
            ? "（已删除）"
            : ExtractDisplayContent(entityType, change.Payload, ref entityTitle);

        // 内容一致：本批推送与远端编辑实际相同，不构成冲突
        if (string.Equals(localContent, remoteContent, StringComparison.Ordinal)
            && change.Action != ChangeActions.Delete)
        {
            return false;
        }

        // 本地侧：当前库中的实体即刚 Push 成功的版本，动作为 Update（覆盖场景）
        string? localPayload = null;
        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            if (note != null && !note.IsDeleted)
            {
                localPayload = SyncPayloadJson.Serialize(
                    SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned));
            }
        }
        else if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            if (folder != null && !folder.IsDeleted)
            {
                localPayload = SyncPayloadJson.Serialize(
                    SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));
            }
        }

        db.SyncConflicts.Add(new SyncConflict
        {
            EntityType = entityType,
            EntityId = entityId,
            EntityTitle = entityTitle,
            LocalContent = localContent,
            RemoteContent = remoteContent,
            LocalAction = ChangeActions.Update,
            RemoteAction = string.IsNullOrEmpty(change.Action) ? ChangeActions.Update : change.Action,
            LocalPayload = localPayload,
            RemotePayload = change.Payload,
            LocalUpdatedAt = localUpdatedAt,
            RemoteUpdatedAt = change.Timestamp != default ? change.Timestamp : DateTime.UtcNow
        });

        return true;
    }

    /// <summary>
    /// 将单个远端变更应用到本地 SQLite 数据库
    /// 支持 Folder 和 Note 两种实体类型的 Create/Update/Delete
    /// </summary>
    internal static void ApplyRemoteChangeToLocalDb(LocalDbContext db, ChangeDto change)
    {
        string entityType = change.EntityType;
        Guid entityId = change.EntityId;
        string action = change.Action;
        string? payload = change.Payload;
        var remoteTimestamp = change.Timestamp != default ? change.Timestamp : DateTime.UtcNow;

        if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            var isNew = folder == null && action != ChangeActions.Delete;
            // Create 或 Update 都需要确保实体存在（upsert 语义）
            if (isNew)
            {
                folder = new LocalFolder { Id = entityId, CreatedAt = remoteTimestamp };
                db.Folders.Add(folder);
            }
            if (folder != null)
            {
                if (action == ChangeActions.Delete)
                {
                    folder.IsDeleted = true;
                    folder.DeletedAt = remoteTimestamp;

                    // F：级联软删本地子孙 folder + 直属 note，保持与服务端一致。
                    CascadeDeleteLocalDescendants(db, entityId, remoteTimestamp);
                }
                else if (payload != null)
                {
                    var root = JsonDocument.Parse(payload).RootElement;
                    folder.Name = SyncPayloadReader.ReadString(root, "name", folder.Name);
                    folder.ParentId = SyncPayloadReader.ReadNullableGuid(root, "parentId", folder.ParentId);
                    folder.IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite", folder.IsFavorite);
                    folder.IsPinned = SyncPayloadReader.ReadBool(root, "isPinned", folder.IsPinned);
                    folder.IsDeleted = false;
                    folder.DeletedAt = null;
                }

                folder.UpdatedAt = remoteTimestamp;
                // 推进实体版本，供编辑器会话判断是否需要从 DB 刷新打开中的笔记/文件夹
                if (change.Version > folder.Version)
                {
                    folder.Version = change.Version;
                }
            }
        }
        else if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            var hasPending = db.PendingChanges.Any(
                p => p.EntityType == EntityTypes.Note && p.EntityId == entityId);
            var isNew = note == null && action != ChangeActions.Delete;
            if (isNew)
            {
                note = new LocalNote { Id = entityId, CreatedAt = remoteTimestamp };
                db.Notes.Add(note);
            }
            if (note != null)
            {
                if (action == ChangeActions.Delete)
                {
                    note.IsDeleted = true;
                    note.DeletedAt = remoteTimestamp;
                }
                else if (payload != null)
                {
                    var root = JsonDocument.Parse(payload).RootElement;
                    var remoteContent = SyncPayloadReader.ReadString(root, "content", note.Content);
                    if (hasPending
                        && string.IsNullOrEmpty(remoteContent)
                        && !string.IsNullOrEmpty(note.Content))
                    {
                        return;
                    }

                    if (hasPending && note.UpdatedAt > remoteTimestamp)
                    {
                        return;
                    }

                    note.Title = SyncPayloadReader.ReadString(root, "title", note.Title);
                    note.Content = remoteContent;
                    note.FolderId = SyncPayloadReader.ReadGuid(root, "folderId", note.FolderId);
                    note.IsFavorite = SyncPayloadReader.ReadBool(root, "isFavorite", note.IsFavorite);
                    note.IsPinned = SyncPayloadReader.ReadBool(root, "isPinned", note.IsPinned);
                    note.IsDeleted = false;
                    note.DeletedAt = null;
                }

                note.UpdatedAt = remoteTimestamp;
                if (change.Version > note.Version)
                {
                    note.Version = change.Version;
                }
            }
        }
    }

    /// <summary>
    /// F：级联软删本地子孙 folder + 直属 note。
    /// 收到远端 folder Delete 时调用，确保本地副本立即一致
    /// </summary>
    internal static void CascadeDeleteLocalDescendants(LocalDbContext db, Guid rootFolderId, DateTime remoteTimestamp)
    {
        // BFS 收集所有子孙 folder（不含 root）
        var allDescendantFolderIds = new List<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var childIds = db.Folders
                .Where(f => f.ParentId == current && !f.IsDeleted)
                .Select(f => f.Id)
                .ToList();
            foreach (var childId in childIds)
            {
                allDescendantFolderIds.Add(childId);
                queue.Enqueue(childId);
            }
        }

        foreach (var folderId in allDescendantFolderIds)
        {
            var folder = db.Folders.Find(folderId);
            if (folder == null || folder.IsDeleted)
            {
                continue;
            }

            folder.IsDeleted = true;
            folder.DeletedAt = remoteTimestamp;
            folder.UpdatedAt = remoteTimestamp;
        }

        // 软删 root 及所有子孙 folder 下的直属 note
        var allDeletedFolderIds = new List<Guid> { rootFolderId }.Concat(allDescendantFolderIds).ToList();
        var notesToDelete = db.Notes
            .Where(n => !n.IsDeleted && allDeletedFolderIds.Contains(n.FolderId))
            .ToList();

        foreach (var note in notesToDelete)
        {
            note.IsDeleted = true;
            note.DeletedAt = remoteTimestamp;
            note.UpdatedAt = remoteTimestamp;
        }
    }
}
