using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>
/// 维护 ClientSyncState / UserSyncMeta，判断客户端是否需要全量重建
/// </summary>
public class ClientSyncStateService
{
    /// <summary>
    /// 客户端版本低于裁剪上界时需要全量重建。
    /// since/clientVersion ≤ 0 表示「开始实体快照」请求，不触发此标志。
    /// </summary>
    public async Task<bool> RequiresFullSyncAsync(AppDbContext db, Guid userId, long clientVersion, CancellationToken ct = default)
    {
        // since=0 走实体快照路径，由 Pull 直接导出当前表态，不能再用 floor 拦截
        if (clientVersion <= 0)
        {
            return false;
        }

        var meta = await db.UserSyncMetas.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId, ct);

        if (meta == null || meta.LogRetentionFloor <= 0)
        {
            return false;
        }

        return clientVersion < meta.LogRetentionFloor;
    }

    /// <summary>Push/Pull 成功后更新设备同步游标</summary>
    public async Task UpsertAsync(
        AppDbContext db,
        Guid userId,
        Guid apiKeyId,
        long syncVersion,
        CancellationToken ct = default)
    {
        if (syncVersion < 0) syncVersion = 0;

        var state = await FindStateAsync(db, apiKeyId, ct);
        if (state != null)
        {
            state.LastSeenAt = DateTime.UtcNow;
            state.IsStaleExcluded = false;
            if (syncVersion > state.LastSyncVersion)
                state.LastSyncVersion = syncVersion;

            return;
        }

        // 无跟踪实体时走原子 upsert，避免并发 version/Pull 双重 Insert
        await UpsertRowAsync(db, userId, apiKeyId, syncVersion, touchOnly: false, ct);
    }

    /// <summary>仅刷新 LastSeenAt（轻量 version 探测）</summary>
    public async Task TouchAsync(AppDbContext db, Guid userId, Guid apiKeyId, CancellationToken ct = default)
    {
        var state = await FindStateAsync(db, apiKeyId, ct);
        if (state != null)
        {
            state.LastSeenAt = DateTime.UtcNow;
            return;
        }

        // 并发 GET /api/sync/version 时两路同时 Add 会触发 ApiKeyId 唯一约束失败
        await UpsertRowAsync(db, userId, apiKeyId, syncVersion: 0, touchOnly: true, ct);
    }

    /// <summary>
    /// SQLite 原子写入：INSERT … ON CONFLICT(ApiKeyId) DO UPDATE。
    /// 参与当前连接上的显式事务（如 Push），避免与 EF Add 竞态。
    /// </summary>
    private static async Task UpsertRowAsync(
        AppDbContext db,
        Guid userId,
        Guid apiKeyId,
        long syncVersion,
        bool touchOnly,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        if (touchOnly)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO ""ClientSyncStates"" (""ApiKeyId"", ""UserId"", ""LastSyncVersion"", ""LastSeenAt"", ""IsStaleExcluded"")
VALUES ({apiKeyId}, {userId}, {0L}, {now}, {0})
ON CONFLICT(""ApiKeyId"") DO UPDATE SET
  ""LastSeenAt"" = excluded.""LastSeenAt"";", ct);
            return;
        }

        await db.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO ""ClientSyncStates"" (""ApiKeyId"", ""UserId"", ""LastSyncVersion"", ""LastSeenAt"", ""IsStaleExcluded"")
VALUES ({apiKeyId}, {userId}, {syncVersion}, {now}, {0})
ON CONFLICT(""ApiKeyId"") DO UPDATE SET
  ""LastSeenAt"" = excluded.""LastSeenAt"",
  ""IsStaleExcluded"" = 0,
  ""LastSyncVersion"" = CASE
    WHEN excluded.""LastSyncVersion"" > ""ClientSyncStates"".""LastSyncVersion""
    THEN excluded.""LastSyncVersion""
    ELSE ""ClientSyncStates"".""LastSyncVersion""
  END;", ct);
    }

    /// <summary>优先查 ChangeTracker，避免 Touch + Upsert 在同一请求内重复读写</summary>
    private static async Task<ClientSyncState?> FindStateAsync(
        AppDbContext db,
        Guid apiKeyId,
        CancellationToken ct)
    {
        var tracked = db.ClientSyncStates.Local.FirstOrDefault(c => c.ApiKeyId == apiKeyId);
        if (tracked != null)
            return tracked;

        return await db.ClientSyncStates.FirstOrDefaultAsync(c => c.ApiKeyId == apiKeyId, ct);
    }

    /// <summary>将长期未同步的设备标记为 stale，使其不再拖住裁剪基线</summary>
    public async Task<int> MarkStaleClientsAsync(AppDbContext db, int staleDays, CancellationToken ct = default)
    {
        if (staleDays <= 0) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-staleDays);
        return await db.ClientSyncStates
            .Where(c => !c.IsStaleExcluded && c.LastSeenAt < cutoff)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.IsStaleExcluded, true),
                ct);
    }

    /// <summary>计算用户 SyncLog 安全裁剪线（活跃设备 LastSyncVersion 最小值）</summary>
    public async Task<long?> ComputeSafeCutoffAsync(AppDbContext db, Guid userId, CancellationToken ct = default)
    {
        return await (
            from c in db.ClientSyncStates
            join k in db.ApiKeys on c.ApiKeyId equals k.Id
            where c.UserId == userId && !c.IsStaleExcluded && k.IsActive
            select (long?)c.LastSyncVersion
        ).MinAsync(ct);
    }

    /// <summary>裁剪成功后更新用户 LogRetentionFloor</summary>
    public virtual async Task UpdateRetentionFloorAsync(
        AppDbContext db,
        Guid userId,
        long cutoff,
        CancellationToken ct = default)
    {
        var meta = await db.UserSyncMetas.FindAsync([userId], ct);
        if (meta == null)
        {
            // 新建元数据时对齐 CurrentVersion，供版本计数器使用
            var currentVersion = await db.SyncLogs
                .Where(s => s.UserId == userId)
                .MaxAsync(s => (long?)s.Version, ct) ?? 0;

            db.UserSyncMetas.Add(new UserSyncMeta
            {
                UserId = userId,
                LogRetentionFloor = cutoff,
                CurrentVersion = currentVersion
            });
            return;
        }

        if (cutoff > meta.LogRetentionFloor)
            meta.LogRetentionFloor = cutoff;
    }
}
