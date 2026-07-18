using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services;

/// <summary>回收站：软删项查询、恢复、过期清理与永久删除</summary>
public class TrashService
{
    private readonly LocalChangeTracker _changeTracker;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly INavigationMutationPublisher _navigationPublisher;

    /// <summary>构造回收站服务</summary>
    public TrashService(
        LocalChangeTracker changeTracker,
        Func<LocalDbContext> dbFactory,
        INavigationMutationPublisher navigationPublisher)
    {
        _changeTracker = changeTracker;
        _dbFactory = dbFactory;
        _navigationPublisher = navigationPublisher;
    }

    /// <summary>回收站列表项</summary>
    public sealed class TrashItem
    {
        /// <summary>实体类型</summary>
        public required string EntityType { get; init; }

        /// <summary>实体 Id</summary>
        public required Guid EntityId { get; init; }

        /// <summary>显示名称</summary>
        public required string DisplayName { get; init; }

        /// <summary>删除时间 UTC</summary>
        public required DateTime DeletedAtUtc { get; init; }

        /// <summary>剩余保留天数</summary>
        public int DaysRemaining { get; init; }
    }

    /// <summary>列出回收站项</summary>
    public List<TrashItem> GetTrashItems()
    {
        var retentionDays = DataSafetySettings.Load().TrashRetentionDays;
        using var db = _dbFactory();
        var items = new List<TrashItem>();

        foreach (var folder in db.Folders.Where(f => f.IsDeleted).ToList())
        {
            var deletedAt = folder.DeletedAt ?? folder.UpdatedAt;
            items.Add(new TrashItem
            {
                EntityType = EntityTypes.Folder,
                EntityId = folder.Id,
                DisplayName = folder.Name,
                DeletedAtUtc = deletedAt,
                DaysRemaining = ComputeDaysRemaining(deletedAt, retentionDays)
            });
        }

        foreach (var note in db.Notes.Where(n => n.IsDeleted).ToList())
        {
            var deletedAt = note.DeletedAt ?? note.UpdatedAt;
            items.Add(new TrashItem
            {
                EntityType = EntityTypes.Note,
                EntityId = note.Id,
                DisplayName = note.Title,
                DeletedAtUtc = deletedAt,
                DaysRemaining = ComputeDaysRemaining(deletedAt, retentionDays)
            });
        }

        return items.OrderByDescending(i => i.DeletedAtUtc).ToList();
    }

    /// <summary>恢复软删项并发布导航增量</summary>
    public void Restore(string entityType, Guid entityId)
    {
        using var db = _dbFactory();

        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            if (note == null || !note.IsDeleted)
            {
                return;
            }

            note.IsDeleted = false;
            note.DeletedAt = null;
            note.UpdatedAt = DateTime.UtcNow;
            db.SaveChangesWithLock();

            _changeTracker.RecordChange(
                EntityTypes.Note,
                note.Id,
                ChangeActions.Update,
                SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned));

            _navigationPublisher.NotifyNoteCreated(note.Id, note.FolderId);
            return;
        }

        if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            if (folder == null || !folder.IsDeleted)
            {
                return;
            }

            folder.IsDeleted = false;
            folder.DeletedAt = null;
            folder.UpdatedAt = DateTime.UtcNow;
            db.SaveChangesWithLock();

            _changeTracker.RecordChange(
                EntityTypes.Folder,
                folder.Id,
                ChangeActions.Update,
                SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));

            _navigationPublisher.NotifyFolderCreated(folder.Id, folder.ParentId);
        }
    }

    /// <summary>
    /// 永久删除；若项曾出现在导航中则发布 Delete delta。
    /// 硬删本地实体前确保保留/写入 Delete pending，避免未同步删除被抹掉后 Pull 复活。
    /// </summary>
    public void PurgePermanently(string entityType, Guid entityId)
    {
        NavigationChange? navChange = null;
        using var db = _dbFactory();

        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            if (note == null)
            {
                return;
            }

            if (!note.IsDeleted)
            {
                navChange = new NavigationChange
                {
                    EntityType = EntityTypes.Note,
                    EntityId = note.Id,
                    Action = ChangeActions.Delete,
                    FolderId = note.FolderId
                };
            }

            EnsureDeletePending(db, EntityTypes.Note, entityId);

            var revisions = db.NoteRevisions.Where(r => r.NoteId == entityId).ToList();
            db.NoteRevisions.RemoveRange(revisions);

            var images = db.Images.Where(i => i.NoteId == entityId).ToList();
            db.Images.RemoveRange(images);

            db.Notes.Remove(note);
            db.SaveChangesWithLock();

            if (navChange != null)
            {
                _navigationPublisher.NotifyChanges(new[] { navChange });
            }

            return;
        }

        if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            if (folder == null)
            {
                return;
            }

            if (!folder.IsDeleted)
            {
                navChange = new NavigationChange
                {
                    EntityType = EntityTypes.Folder,
                    EntityId = folder.Id,
                    Action = ChangeActions.Delete
                };
            }

            EnsureDeletePending(db, EntityTypes.Folder, entityId);

            db.Folders.Remove(folder);
            db.SaveChangesWithLock();

            if (navChange != null)
            {
                _navigationPublisher.NotifyChanges(new[] { navChange });
            }
        }
    }

    /// <summary>
    /// 硬删前保证存在 Delete 待推送：已有 Delete 则保留；Create/Update 则改为 Delete。
    /// </summary>
    private static void EnsureDeletePending(LocalDbContext db, string entityType, Guid entityId)
    {
        var pending = db.PendingChanges
            .FirstOrDefault(p => p.EntityType == entityType && p.EntityId == entityId);

        if (pending != null)
        {
            if (pending.Action == ChangeActions.Delete)
            {
                return;
            }

            pending.Action = ChangeActions.Delete;
            pending.Payload = null;
            pending.UpdatedAt = DateTime.UtcNow;
            return;
        }

        db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = ChangeActions.Delete,
            Payload = null,
            UpdatedAt = DateTime.UtcNow
        });
    }

    /// <summary>删除超过保留期的回收站项（本地硬删）</summary>
    public int PurgeExpired(bool publishNavigation = false)
    {
        var settings = DataSafetySettings.Load();
        var cutoff = DateTime.UtcNow.AddDays(-settings.TrashRetentionDays);
        var purged = 0;

        using var db = _dbFactory();

        foreach (var note in db.Notes.Where(n => n.IsDeleted).ToList())
        {
            var deletedAt = note.DeletedAt ?? note.UpdatedAt;
            if (deletedAt >= cutoff)
            {
                continue;
            }

            PurgePermanently(EntityTypes.Note, note.Id);
            purged++;
        }

        foreach (var folder in db.Folders.Where(f => f.IsDeleted).ToList())
        {
            var deletedAt = folder.DeletedAt ?? folder.UpdatedAt;
            if (deletedAt >= cutoff)
            {
                continue;
            }

            PurgePermanently(EntityTypes.Folder, folder.Id);
            purged++;
        }

        if (purged > 0)
        {
            LogHelper.Info("回收站已自动清理 {0} 条过期项（保留 {1} 天）", purged, settings.TrashRetentionDays);
        }

        return purged;
    }

    /// <summary>清空回收站</summary>
    public void EmptyTrash()
    {
        var items = GetTrashItems();
        foreach (var item in items)
        {
            PurgePermanently(item.EntityType, item.EntityId);
        }
    }

    private static int ComputeDaysRemaining(DateTime deletedAtUtc, int retentionDays)
    {
        var expiresAt = deletedAtUtc.AddDays(retentionDays);
        var remaining = (expiresAt - DateTime.UtcNow).TotalDays;
        return Math.Max(0, (int)Math.Ceiling(remaining));
    }
}
