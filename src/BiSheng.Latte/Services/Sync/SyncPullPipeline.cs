using BiSheng.Latte.Data;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services.Sync;

/// <summary>
/// Pull 阶段：从服务端拉取增量变更
/// </summary>
internal sealed class SyncPullPipeline
{
    /// <summary>Pull 分页：每批最多读取的 SyncLog 条数（与服务端 MaxPullLimit 对齐）</summary>
    internal const int PullPageSize = 200;

    private readonly ApiClient _apiClient;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly Action<int> _onConflictsDetected;
    private readonly Func<Task> _performFullResync;

    /// <summary>创建 Pull 管道</summary>
    internal SyncPullPipeline(
        ApiClient apiClient,
        Func<LocalDbContext> dbFactory,
        Action<int> onConflictsDetected,
        Func<Task> performFullResync)
    {
        _apiClient = apiClient;
        _dbFactory = dbFactory;
        _onConflictsDetected = onConflictsDetected;
        _performFullResync = performFullResync;
    }

    /// <summary>
    /// 从服务端拉上次同步版本之后的变更（分页循环直至 HasMore=false）
    /// </summary>
    internal async Task<SyncNavigationDelta> PullChangesAsync()
    {
        var aggregatedNav = new List<NavigationChange>();

        while (true)
        {
            using var db = _dbFactory();
            var state = db.SyncState.Find(1);
            var sinceVersion = state?.LastSyncVersion ?? 0;

            var result = await _apiClient.GetAsync<SyncPullResponse>(
                $"/api/sync/pull?since={sinceVersion}&limit={PullPageSize}");

            if (result?.RequiresFullSync == true)
            {
                await _performFullResync();
                return SyncNavigationDelta.FullRefresh;
            }

            if (result == null)
            {
                break;
            }

            if (result.Changes != null && result.Changes.Count > 0)
            {
                aggregatedNav.AddRange(NavigationDeltaBuilder.FromChangeDtos(_dbFactory, result.Changes));

                var conflictsCreated = 0;
                foreach (var change in result.Changes)
                {
                    if (SyncRemoteChangeMerger.MergeRemoteChange(db, change))
                    {
                        conflictsCreated++;
                    }
                }

                if (conflictsCreated > 0)
                {
                    var unresolvedCount = db.SyncConflicts.Count(c => !c.IsResolved);
                    _onConflictsDetected(unresolvedCount);
                }
            }

            var nextCursor = result.HasMore ? result.NextSince : result.ServerVersion;
            SyncPendingOrdering.UpsertLastSyncVersion(db, nextCursor);
            db.SaveChangesWithLock();

            if (!result.HasMore)
            {
                break;
            }

            // 防御未推进则避免死循环（异常服务端响应）
            if (nextCursor <= sinceVersion)
            {
                LogHelper.Warn("Pull 分页游标未推进（since={0}, next={1}），停止循环", sinceVersion, nextCursor);
                break;
            }
        }

        if (aggregatedNav.Count == 0)
        {
            return SyncNavigationDelta.Empty;
        }

        return SyncNavigationDelta.FromChanges(aggregatedNav);
    }
}
