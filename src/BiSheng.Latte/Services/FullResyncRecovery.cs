using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services;

/// <summary>
/// 全量重建前抢救 PendingChanges 及实体快照，Pull 完成后合并未上云的本地数据
/// </summary>
public static class FullResyncRecovery
{
    /// <summary>抢救快照</summary>
    public sealed class RescueSnapshot
    {
        public DateTime CapturedAtUtc { get; init; }

        public List<RescuedEntry> Entries { get; init; } = new();
    }

    public sealed class RescuedEntry
    {
        public required string EntityType { get; init; }

        public required Guid EntityId { get; init; }

        public required string Action { get; init; }

        public string? Payload { get; init; }

        public DateTime UpdatedAt { get; init; }

        public LocalNote? Note { get; init; }

        public LocalFolder? Folder { get; init; }
    }

    /// <summary>在全量清库前采集待推送队列及实体快照</summary>
    public static RescueSnapshot Capture(LocalDbContext db)
    {
        var snapshot = new RescueSnapshot { CapturedAtUtc = DateTime.UtcNow };

        foreach (var pending in db.PendingChanges.ToList())
        {
            LocalNote? noteSnapshot = null;
            LocalFolder? folderSnapshot = null;

            if (pending.EntityType == EntityTypes.Note)
            {
                var note = db.Notes.Find(pending.EntityId);
                if (note != null)
                {
                    noteSnapshot = Clone(note);
                }
            }
            else if (pending.EntityType == EntityTypes.Folder)
            {
                var folder = db.Folders.Find(pending.EntityId);
                if (folder != null)
                {
                    folderSnapshot = Clone(folder);
                }
            }

            snapshot.Entries.Add(new RescuedEntry
            {
                EntityType = pending.EntityType,
                EntityId = pending.EntityId,
                Action = pending.Action,
                Payload = pending.Payload,
                UpdatedAt = pending.UpdatedAt,
                Note = noteSnapshot,
                Folder = folderSnapshot
            });
        }

        return snapshot;
    }

    /// <summary>Pull 完成后恢复仍未与服务端收敛的本地变更</summary>
    public static int ApplyAfterPull(LocalDbContext db, RescueSnapshot snapshot)
    {
        var restored = 0;

        foreach (var entry in snapshot.Entries)
        {
            if (!ShouldRestore(db, entry))
            {
                continue;
            }

            RestoreEntity(db, entry);
            ReenqueuePending(db, entry);
            restored++;
        }

        if (restored > 0)
        {
            LogHelper.Warn("全量同步后已恢复 {0} 条未上云本地变更", restored);
        }

        return restored;
    }

    private static bool ShouldRestore(LocalDbContext db, RescuedEntry entry)
    {
        if (entry.Action == ChangeActions.Delete)
        {
            return IsEntityActive(db, entry.EntityType, entry.EntityId);
        }

        if (string.IsNullOrEmpty(entry.Payload))
        {
            return entry.Note != null || entry.Folder != null;
        }

        var currentPayload = BuildPayloadJson(db, entry.EntityType, entry.EntityId);
        if (currentPayload == null)
        {
            return entry.Note != null || entry.Folder != null;
        }

        return !SyncPayloadFingerprint.AreEquivalent(entry.EntityType, entry.Payload, currentPayload);
    }

    private static bool IsEntityActive(LocalDbContext db, string entityType, Guid entityId)
    {
        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            return note != null && !note.IsDeleted;
        }

        if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            return folder != null && !folder.IsDeleted;
        }

        return false;
    }

    private static string? BuildPayloadJson(LocalDbContext db, string entityType, Guid entityId)
    {
        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            if (note == null || note.IsDeleted)
            {
                return null;
            }

            return SyncPayloadJson.Serialize(
                SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned));
        }

        if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            if (folder == null || folder.IsDeleted)
            {
                return null;
            }

            return SyncPayloadJson.Serialize(
                SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));
        }

        return null;
    }

    private static void RestoreEntity(LocalDbContext db, RescuedEntry entry)
    {
        if (entry.EntityType == EntityTypes.Note && entry.Note != null)
        {
            var note = db.Notes.Find(entry.EntityId);
            if (note == null)
            {
                db.Notes.Add(Clone(entry.Note));
                return;
            }

            CopyNoteFields(note, entry.Note);
            return;
        }

        if (entry.EntityType == EntityTypes.Folder && entry.Folder != null)
        {
            var folder = db.Folders.Find(entry.EntityId);
            if (folder == null)
            {
                db.Folders.Add(Clone(entry.Folder));
                return;
            }

            CopyFolderFields(folder, entry.Folder);
        }
    }

    private static void ReenqueuePending(LocalDbContext db, RescuedEntry entry)
    {
        var existing = db.PendingChanges
            .FirstOrDefault(p => p.EntityType == entry.EntityType && p.EntityId == entry.EntityId);
        if (existing != null)
        {
            db.PendingChanges.Remove(existing);
        }

        db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = entry.EntityType,
            EntityId = entry.EntityId,
            Action = entry.Action,
            Payload = entry.Payload,
            UpdatedAt = entry.UpdatedAt
        });
    }

    private static LocalNote Clone(LocalNote source) => new()
    {
        Id = source.Id,
        Title = source.Title,
        Content = source.Content,
        FolderId = source.FolderId,
        IsFavorite = source.IsFavorite,
        IsPinned = source.IsPinned,
        IsDeleted = source.IsDeleted,
        DeletedAt = source.DeletedAt,
        Version = source.Version,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static LocalFolder Clone(LocalFolder source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        ParentId = source.ParentId,
        IsFavorite = source.IsFavorite,
        IsPinned = source.IsPinned,
        IsDeleted = source.IsDeleted,
        DeletedAt = source.DeletedAt,
        Version = source.Version,
        CreatedAt = source.CreatedAt,
        UpdatedAt = source.UpdatedAt
    };

    private static void CopyNoteFields(LocalNote target, LocalNote source)
    {
        target.Title = source.Title;
        target.Content = source.Content;
        target.FolderId = source.FolderId;
        target.IsFavorite = source.IsFavorite;
        target.IsPinned = source.IsPinned;
        target.IsDeleted = source.IsDeleted;
        target.DeletedAt = source.DeletedAt;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static void CopyFolderFields(LocalFolder target, LocalFolder source)
    {
        target.Name = source.Name;
        target.ParentId = source.ParentId;
        target.IsFavorite = source.IsFavorite;
        target.IsPinned = source.IsPinned;
        target.IsDeleted = source.IsDeleted;
        target.DeletedAt = source.DeletedAt;
        target.UpdatedAt = source.UpdatedAt;
    }
}
