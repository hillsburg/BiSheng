using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Services;

/// <summary>
/// 本地变更追踪器：将用户操作记录到本地待推送队列
///
/// 设计原则：
/// - 纯本地 DB 操作，不依赖任何网络服务
/// - 与 SyncService 完全解耦：本类只负责"记录什么变了",
///   SyncService 独立负责"何时/如何把变更推送到云端"
/// - 所有 ViewModel 只依赖此服务，不直接依赖 SyncService
///
/// 去重合并策略：同一实体的多次变更只保留最新一条，
/// 避免推送多条 Update 记录到服务端
/// </summary>
public class LocalChangeTracker
{
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly LocalEditJournalService? _editJournal;

    /// <summary>
    /// 当有新的本地变更被记录时触发
    /// 同步引擎可订阅此事件来触发防抖推送
    /// </summary>
    public event Action? OnChangeRecorded;

    public LocalChangeTracker(Func<LocalDbContext> dbFactory, LocalEditJournalService? editJournal = null)
    {
        _dbFactory = dbFactory;
        _editJournal = editJournal;
    }

    /// <summary>
    /// 记录一个本地变更到待推送队列（独立 DbContext + 单次 SaveChanges）
    /// </summary>
    public void RecordChange(string entityType, Guid entityId, string action, object? payload = null)
    {
        using var db = _dbFactory();
        ApplyPendingChange(db, entityType, entityId, action, payload);
        db.SaveChangesWithLock();
        NotifyChangeRecorded();
    }

    /// <summary>
    /// 在已有 DbContext 上合并待推送变更（不 SaveChanges，便于与笔记正文同事务写入）
    /// </summary>
    public void ApplyPendingChange(
        LocalDbContext db,
        string entityType,
        Guid entityId,
        string action,
        object? payload = null,
        string? journalTitle = null,
        string? journalContent = null)
    {
        var existing = db.PendingChanges
            .FirstOrDefault(p => p.EntityType == entityType && p.EntityId == entityId);

        if (existing != null)
        {
            existing.Action = action == ChangeActions.Delete ? ChangeActions.Delete :
                existing.Action == ChangeActions.Create ? ChangeActions.Create : action;
            existing.Payload = payload != null ? SyncPayloadJson.Serialize(payload) : existing.Payload;
            existing.UpdatedAt = DateTime.UtcNow;
            db.PendingChanges.Update(existing);
        }
        else
        {
            db.PendingChanges.Add(new LocalPendingChange
            {
                EntityType = entityType,
                EntityId = entityId,
                Action = action,
                Payload = payload != null ? SyncPayloadJson.Serialize(payload) : null,
                UpdatedAt = DateTime.UtcNow
            });
        }

        _editJournal?.Append(db, entityType, entityId, action, journalTitle, journalContent);
    }

    /// <summary>当前待推送变更条数（供连接状态徽章展示）</summary>
    public int GetPendingChangeCount()
    {
        using var db = _dbFactory();
        return db.PendingChanges.Count();
    }

    /// <summary>通知同步引擎有新的本地变更（与 ApplyPendingChange 分离，便于合并事务后只触发一次）</summary>
    public void NotifyChangeRecorded() => OnChangeRecorded?.Invoke();
}
