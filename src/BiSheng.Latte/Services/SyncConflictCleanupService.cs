using BiSheng.Latte.Data;

namespace BiSheng.Latte.Services;

/// <summary>
/// 清理已解决的同步冲突记录，避免 LocalContent/Payload 多副本长期膨胀
/// </summary>
public class SyncConflictCleanupService
{
    /// <summary>已解决冲突保留天数（按 CreatedAt 计算）</summary>
    public const int ResolvedRetentionDays = 30;

    private readonly Func<LocalDbContext> _dbFactory;

    public SyncConflictCleanupService(Func<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>启动时裁剪过期的已解决冲突</summary>
    public void PruneIfNeeded()
    {
        using var db = _dbFactory();
        Prune(db, DateTime.UtcNow.AddDays(-ResolvedRetentionDays));
    }

    /// <summary>测试与内部共用：删除 CreatedAt 早于 cutoff 的已解决冲突</summary>
    internal static int Prune(LocalDbContext db, DateTime cutoffUtc)
    {
        var stale = db.SyncConflicts
            .Where(c => c.IsResolved && c.CreatedAt < cutoffUtc)
            .ToList();
        if (stale.Count == 0)
        {
            return 0;
        }

        db.SyncConflicts.RemoveRange(stale);
        db.SaveChangesWithLock();
        LogHelper.Debug(
            "已解决冲突已裁剪 {0} 条（保留 {1} 天）",
            stale.Count,
            ResolvedRetentionDays);
        return stale.Count;
    }
}
