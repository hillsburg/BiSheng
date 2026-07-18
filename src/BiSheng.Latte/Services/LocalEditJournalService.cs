using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using BiSheng.Shared;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Services;

/// <summary>
/// 本地只追加编辑日志：记录每次本地变更，Push 成功后打标，按策略裁剪
/// </summary>
public class LocalEditJournalService
{
    private readonly Func<LocalDbContext> _dbFactory;

    public LocalEditJournalService(Func<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>在已有 DbContext 上追加一条日志（不 SaveChanges）</summary>
    public void Append(
        LocalDbContext db,
        string entityType,
        Guid entityId,
        string action,
        string? title = null,
        string? content = null)
    {
        var settings = DataSafetySettings.Load();
        if (!settings.EnableEditJournal)
        {
            return;
        }

        string? hash = null;
        if (entityType == EntityTypes.Note
            && action != ChangeActions.Delete
            && title != null
            && content != null)
        {
            hash = NoteContentHash.Compute(title, content);
        }

        db.EditJournal.Add(new LocalEditJournalEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            ContentHash = hash,
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    /// <summary>Push 成功后标记对应实体的未同步日志</summary>
    public void MarkSynced(IEnumerable<(string EntityType, Guid EntityId)> items)
    {
        var settings = DataSafetySettings.Load();
        if (!settings.EnableEditJournal)
        {
            return;
        }

        var syncedAt = DateTime.UtcNow;
        using var db = _dbFactory();

        foreach (var (entityType, entityId) in items)
        {
            var pendingEntries = db.EditJournal
                .Where(j => j.EntityType == entityType
                            && j.EntityId == entityId
                            && j.SyncedAtUtc == null)
                .ToList();

            foreach (var entry in pendingEntries)
            {
                entry.SyncedAtUtc = syncedAt;
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            db.SaveChangesWithLock();
        }

        Prune(db, settings);
    }

    /// <summary>启动或写入后裁剪过期条目</summary>
    public void PruneIfNeeded()
    {
        var settings = DataSafetySettings.Load();
        if (!settings.EnableEditJournal)
        {
            return;
        }

        using var db = _dbFactory();
        Prune(db, settings);
    }

    private static void Prune(LocalDbContext db, DataSafetySettings settings)
    {
        var cutoff = DateTime.UtcNow.AddDays(-settings.EditJournalRetentionDays);
        var stale = db.EditJournal.Where(j => j.CreatedAtUtc < cutoff).ToList();
        if (stale.Count == 0)
        {
            return;
        }

        db.EditJournal.RemoveRange(stale);
        db.SaveChangesWithLock();
        LogHelper.Debug("编辑日志已裁剪 {0} 条（保留 {1} 天）", stale.Count, settings.EditJournalRetentionDays);
    }
}
