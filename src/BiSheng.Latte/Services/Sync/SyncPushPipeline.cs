using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services.Sync;

/// <summary>
/// Push 阶段：将本地待推送队列发送到服务端
/// </summary>
internal sealed class SyncPushPipeline
{
    private readonly ApiClient _apiClient;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly LocalEditJournalService? _editJournal;
    private readonly Action<int> _onConflictsDetected;
    private readonly Func<Task> _performFullResync;
    private readonly Action _markFullResyncOccurred;

    /// <summary>创建 Push 管道</summary>
    internal SyncPushPipeline(
        ApiClient apiClient,
        Func<LocalDbContext> dbFactory,
        LocalEditJournalService? editJournal,
        Action<int> onConflictsDetected,
        Func<Task> performFullResync,
        Action markFullResyncOccurred)
    {
        _apiClient = apiClient;
        _dbFactory = dbFactory;
        _editJournal = editJournal;
        _onConflictsDetected = onConflictsDetected;
        _performFullResync = performFullResync;
        _markFullResyncOccurred = markFullResyncOccurred;
    }

    /// <summary>
    /// 将本地待推送队列中的变更发送到服务端
    /// 只清空推送成功的 pending，保留失败的继续重试
    /// 返回 true 表示有推送操作（无论成功失败），false 表示无待推送
    /// </summary>
    internal async Task<bool> PushPendingChangesAsync()
    {
        using var db = _dbFactory();
        SyncPendingOrdering.EnsurePendingIncludesDependencies(db);
        var (pending, cyclicFolderIds) = SyncPendingOrdering.OrderPendingForPush(db.PendingChanges.ToList());

        // M：成环的 folder 本地保留 pending 不推送，记日志提示用户修复父子关系
        if (cyclicFolderIds.Count > 0)
        {
            LogHelper.Warn("检测到文件夹父子关系成环，已跳过 {0} 条：{1}",
                cyclicFolderIds.Count, string.Join(", ", cyclicFolderIds));
        }

        if (pending.Count == 0) return false;

        var state = db.SyncState.Find(1);
        var clientVersion = state?.LastSyncVersion ?? 0;

        var response = await _apiClient.PostAsync<SyncPushResponse>("/api/sync/push", new
        {
            clientVersion,
            changes = pending
        });

        if (response?.RequiresFullSync == true)
        {
            await _performFullResync();
            _markFullResyncOccurred();
            return true;
        }

        if (response != null)
        {
            // D：事务整体回滚——服务端返回 TransactionRolledBack=true，
            // 此时 FailedEntityIds 只含回滚前已失败的项，"已应用"的变更实际也回滚了，
            // 不能清空任何 pending，也不能推进游标，全部留待下次重试
            if (response.TransactionRolledBack)
            {
                LogHelper.Warn("Push 事务回滚，保留所有 {0} 条 pending 下次重试", pending.Count);
                return true;
            }

            // 只清空推送成功的 pending，保留失败的继续重试
            var failedIds = response.FailedEntityIds ?? new();
            var succeeded = pending.Where(p => !failedIds.Contains(p.EntityId)).ToList();
            var failed = pending.Where(p => failedIds.Contains(p.EntityId)).ToList();

            if (succeeded.Count > 0)
            {
                SyncPendingOrdering.RemovePendingChanges(db, succeeded);
                _editJournal?.MarkSynced(succeeded.Select(p => (p.EntityType, p.EntityId)));
            }

            if (failed.Count > 0)
            {
                LogHelper.Warn("Push 部分失败：{0} 条保留重试", failed.Count);
            }

            // 如果服务端返回了冲突变更（客户端 version 落后于服务端），走冲突检测流程
            var conflictsCreated = 0;

            if (response.ConflictingChanges != null && response.ConflictingChanges.Count > 0)
            {
                foreach (var change in response.ConflictingChanges)
                {
                    if (SyncRemoteChangeMerger.MergeRemoteChange(db, change))
                    {
                        conflictsCreated++;
                    }
                }
            }

            // E：冲突盲区——处理本批覆盖的远端 pre-state
            if (response.OverwrittenChanges != null && response.OverwrittenChanges.Count > 0)
            {
                foreach (var change in response.OverwrittenChanges)
                {
                    if (SyncRemoteChangeMerger.RecordConflictFromOverwritten(db, change))
                    {
                        conflictsCreated++;
                    }
                }
            }

            SyncPendingOrdering.UpsertLastSyncVersion(db, response.ServerVersion);
            db.SaveChangesWithLock();

            if (conflictsCreated > 0)
            {
                var unresolvedCount = db.SyncConflicts.Count(c => !c.IsResolved);
                _onConflictsDetected(unresolvedCount);
            }
        }

        return true;
    }
}
