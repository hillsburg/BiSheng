using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services.Sync;

/// <summary>
/// 全量重建：服务端要求 RequiresFullSync 时清库并以 since=0 拉实体快照
/// </summary>
internal sealed class SyncFullResyncRunner
{
    private readonly ApiClient _apiClient;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly Action<SyncStatus, string> _onSyncStatusChanged;

    /// <summary>创建全量同步执行器</summary>
    internal SyncFullResyncRunner(
        ApiClient apiClient,
        Func<LocalDbContext> dbFactory,
        Action<SyncStatus, string> onSyncStatusChanged)
    {
        _apiClient = apiClient;
        _dbFactory = dbFactory;
        _onSyncStatusChanged = onSyncStatusChanged;
    }

    /// <summary>
    /// 服务端 SyncLog 已裁剪且本地版本过旧：抢救 Pending 落盘后清库，再以 since=0 实体快照重建。
    /// 若磁盘上已有未完成的抢救文件（上次中断），优先使用磁盘快照。
    /// </summary>
    internal async Task PerformFullResyncAsync()
    {
        LogHelper.Warn("服务端要求全量同步，正在重建本地数据副本");
        _onSyncStatusChanged(SyncStatus.Pulling, "全量同步中…");

        FullResyncRecovery.RescueSnapshot rescue;
        using (var db = _dbFactory())
        {
            // 中断恢复：磁盘快照优先，避免空库 Capture 覆盖未上云数据
            rescue = FullResyncRescueStore.TryLoad() ?? FullResyncRecovery.Capture(db);

            // 清库前必须落盘；崩溃后可由启动路径 Resume
            if (rescue.Entries.Count > 0)
            {
                FullResyncRescueStore.Save(rescue);
                LogHelper.Info("全量同步抢救快照已落盘，共 {0} 条", rescue.Entries.Count);
            }

            db.Notes.RemoveRange(db.Notes.ToList());
            db.Folders.RemoveRange(db.Folders.ToList());
            db.PendingChanges.RemoveRange(db.PendingChanges.ToList());

            var syncState = db.SyncState.Find(1);
            if (syncState == null)
            {
                db.SyncState.Add(new LocalSyncState { Id = 1, LastSyncVersion = 0 });
            }
            else
            {
                syncState.LastSyncVersion = 0;
            }

            db.SaveChangesWithLock();
        }

        try
        {
            var completed = await PullEntitySnapshotAfterFullResetAsync(rescue);
            if (completed)
            {
                FullResyncRescueStore.Clear();
            }
            else
            {
                LogHelper.Warn("全量同步未完整完成，保留抢救文件以便下次恢复");
            }
        }
        catch
        {
            // 保留落盘快照，供下次启动或再次全量重建恢复
            throw;
        }
    }

    /// <summary>
    /// 清库后分页拉取实体快照，并合并抢救的未上云本地变更。
    /// </summary>
    /// <returns>是否完整拉完并已合并抢救（失败时保留磁盘抢救文件）</returns>
    internal async Task<bool> PullEntitySnapshotAfterFullResetAsync(FullResyncRecovery.RescueSnapshot? rescue = null)
    {
        using var db = _dbFactory();
        long snapshotOffset = 0;
        var pullCompleted = false;

        while (true)
        {
            var result = await _apiClient.GetAsync<SyncPullResponse>(
                $"/api/sync/pull?since=0&limit={SyncPullPipeline.PullPageSize}&snapshotOffset={snapshotOffset}");

            if (result == null)
            {
                LogHelper.Warn("实体快照 Pull 返回空响应，中止");
                return false;
            }

            if (result.RequiresFullSync)
            {
                // 契约保证 since=0 不应再要求全量；防御性中止避免死循环
                LogHelper.Warn("实体快照 Pull 仍返回 RequiresFullSync，中止");
                return false;
            }

            if (result.Changes != null && result.Changes.Count > 0)
            {
                foreach (var change in result.Changes)
                {
                    SyncRemoteChangeMerger.ApplyRemoteChangeToLocalDb(db, change);
                }
            }

            if (!result.HasMore)
            {
                SyncPendingOrdering.UpsertLastSyncVersion(db, result.ServerVersion);
                db.SaveChangesWithLock();
                pullCompleted = true;
                break;
            }

            // 快照中间页：NextSince 是下一 snapshotOffset，不推进 LastSyncVersion
            if (result.IsEntitySnapshot)
            {
                var nextOffset = result.NextSince;
                if (nextOffset <= snapshotOffset)
                {
                    LogHelper.Warn(
                        "实体快照分页游标未推进（offset={0}, next={1}），停止循环",
                        snapshotOffset, nextOffset);
                    return false;
                }

                snapshotOffset = nextOffset;
                db.SaveChangesWithLock();
                continue;
            }

            // 兼容旧服务端：若未标记快照，按 NextSince 推进（可能不完整，仅兜底）
            var sinceFallback = db.SyncState.Find(1)?.LastSyncVersion ?? 0;
            SyncPendingOrdering.UpsertLastSyncVersion(db, result.NextSince);
            db.SaveChangesWithLock();
            if (result.NextSince <= sinceFallback)
            {
                return false;
            }

            snapshotOffset = 0;
        }

        if (!pullCompleted)
        {
            return false;
        }

        if (rescue != null && rescue.Entries.Count > 0)
        {
            FullResyncRecovery.ApplyAfterPull(db, rescue);
            db.SaveChangesWithLock();
        }

        return true;
    }
}
