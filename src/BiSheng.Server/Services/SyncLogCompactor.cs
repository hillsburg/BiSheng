using BiSheng.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>
/// SyncLog 单用户裁剪：删除旧日志与抬高 LogRetentionFloor 必须同事务提交，
/// 避免「日志已删但 floor 未抬」导致落后客户端静默丢变更。
/// </summary>
public static class SyncLogCompactor
{
    /// <summary>
    /// 删除 Version &lt; cutoff 的 SyncLog，并在同一事务中更新 LogRetentionFloor。
    /// </summary>
    /// <returns>实际删除的行数；无可删行时返回 0 且不改 floor</returns>
    public static async Task<int> CompactUserAsync(
        AppDbContext db,
        ClientSyncStateService clientSync,
        Guid userId,
        long cutoff,
        CancellationToken ct = default)
    {
        if (cutoff <= 0)
        {
            return 0;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // 先删日志再抬 floor：二者同事务提交，失败则整批回滚
            var deleted = await db.SyncLogs
                .Where(s => s.UserId == userId && s.Version < cutoff)
                .ExecuteDeleteAsync(ct);

            if (deleted <= 0)
            {
                await tx.RollbackAsync(ct);
                return 0;
            }

            await clientSync.UpdateRetentionFloorAsync(db, userId, cutoff, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return deleted;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
