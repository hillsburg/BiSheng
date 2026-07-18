using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BiSheng.Server.Services;

/// <summary>
/// 用户级 SyncLog 版本号分配：通过 UserSyncMeta.CurrentVersion 原子递增，替代 MAX(Version)+1
/// </summary>
public class UserSyncVersionService
{
    /// <summary>
    /// 读取当前已分配的最高版本号
    /// </summary>
    /// <param name="db">数据库上下文</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task<long> GetCurrentVersionAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct = default)
    {
        await EnsureInitializedAsync(db, userId, ct);
        return await db.UserSyncMetas
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.CurrentVersion)
            .SingleAsync(ct);
    }

    /// <summary>
    /// 原子分配下一个版本号
    /// </summary>
    /// <param name="db">数据库上下文</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="ct">取消令牌</param>
    public Task<long> ReserveNextVersionAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct = default) =>
        ReserveVersionsAsync(db, userId, 1, ct);

    /// <summary>
    /// 原子分配连续 count 个版本号，返回起始版本
    /// </summary>
    /// <param name="db">数据库上下文</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="count">需要预留的版本数量</param>
    /// <param name="ct">取消令牌</param>
    public async Task<long> ReserveVersionsAsync(
        AppDbContext db,
        Guid userId,
        int count,
        CancellationToken ct = default)
    {
        if (count <= 0)
        {
            return await GetCurrentVersionAsync(db, userId, ct);
        }

        await EnsureInitializedAsync(db, userId, ct);

        // 用 ADO.NET 直接执行 UPDATE … RETURNING：
        // 1. EF Core 的 SqlQuery 对 non-composable SQL（UPDATE…RETURNING）会抛 compose 异常
        // 2. SqlQuery 的 Guid 参数序列化格式可能与实体查询存入的不一致，导致 WHERE 不匹配
        // 直接用 DbCommand 挂到当前事务，显式控制参数类型，单语句原子递增
        var connection = db.Database.GetDbConnection();
        var ownsConnection = connection.State != System.Data.ConnectionState.Open;
        if (ownsConnection)
        {
            await connection.OpenAsync(ct);
        }

        try
        {
            using var cmd = connection.CreateCommand();
            var tx = db.Database.CurrentTransaction?.GetDbTransaction();
            if (tx != null)
            {
                cmd.Transaction = tx;
            }

            cmd.CommandText = """
                UPDATE "UserSyncMetas"
                SET "CurrentVersion" = "CurrentVersion" + @count
                WHERE "UserId" = @userId
                RETURNING "CurrentVersion"
                """;

            var countParam = cmd.CreateParameter();
            countParam.ParameterName = "@count";
            countParam.DbType = System.Data.DbType.Int64;
            countParam.Value = count;
            cmd.Parameters.Add(countParam);

            // Guid 以原生类型传参，Microsoft.Data.Sqlite 默认按 BLOB 处理，与 EF Core 实体查询存入格式一致
            var userIdParam = cmd.CreateParameter();
            userIdParam.ParameterName = "@userId";
            userIdParam.Value = userId;
            cmd.Parameters.Add(userIdParam);

            var scalar = await cmd.ExecuteScalarAsync(ct);
            if (scalar == null)
            {
                // UPDATE 未匹配任何行：UserSyncMeta 不存在（EnsureInitializedAsync 之后被并发删除？）
                throw new InvalidOperationException(
                    $"UserSyncMeta not found for user {userId} during version reservation");
            }
            var newCurrentVersion = (long)scalar;
            // RETURNING 返回递增后的计数器，反推本批起始版本
            return newCurrentVersion - count + 1;
        }
        finally
        {
            if (ownsConnection)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// 确保 UserSyncMeta 存在，CurrentVersion 与现有 SyncLog 对齐
    /// </summary>
    /// <param name="db">数据库上下文</param>
    /// <param name="userId">用户 ID</param>
    /// <param name="ct">取消令牌</param>
    public async Task EnsureInitializedAsync(
        AppDbContext db,
        Guid userId,
        CancellationToken ct = default)
    {
        // 快路径：已存在直接返回，避免每次 Push 都执行 INSERT
        if (await db.UserSyncMetas.AnyAsync(m => m.UserId == userId, ct))
        {
            return;
        }

        // L：并发首次写竞态——两个请求同时通过 AnyAsync=false 后都 Add 会 PK 冲突。
        // 用 INSERT OR IGNORE 原子插入：已存在则跳过，不抛异常。
        // CurrentVersion 从历史 SyncLog 回填，避免与已有版本号冲突。
        var maxFromLogs = await db.SyncLogs
            .Where(s => s.UserId == userId)
            .MaxAsync(s => (long?)s.Version, ct) ?? 0;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT OR IGNORE INTO "UserSyncMetas" ("UserId", "CurrentVersion", "LogRetentionFloor")
            VALUES ({userId}, {maxFromLogs}, 0)
            """, ct);
    }
}
