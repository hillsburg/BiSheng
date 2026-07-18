using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>SyncLog 裁剪与 LogRetentionFloor 同事务</summary>
public class SyncLogCompactorTests
{
    /// <summary>成功裁剪时同时删除旧日志并抬高 floor</summary>
    [Fact]
    public async Task CompactUser_DeletesLogsAndRaisesFloor()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, _, _) = fixture.SeedUserWithNote();

        SeedSyncLogs(fixture, userId, count: 10);
        fixture.Db.ClientSyncStates.Add(new ClientSyncState
        {
            ApiKeyId = apiKeyId,
            UserId = userId,
            LastSyncVersion = 5,
            LastSeenAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var clientSync = new ClientSyncStateService();
        var deleted = await SyncLogCompactor.CompactUserAsync(
            fixture.Db, clientSync, userId, cutoff: 5);

        Assert.Equal(4, deleted);
        Assert.Equal(0, await fixture.Db.SyncLogs.CountAsync(s => s.UserId == userId && s.Version < 5));
        Assert.Equal(6, await fixture.Db.SyncLogs.CountAsync(s => s.UserId == userId && s.Version >= 5));

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        Assert.Equal(5, meta.LogRetentionFloor);
    }

    /// <summary>无可删行时不改 floor</summary>
    [Fact]
    public async Task CompactUser_WhenNothingToDelete_LeavesFloorUnchanged()
    {
        using var fixture = new TestDbFactory();
        var (userId, _, _, _) = fixture.SeedUserWithNote();

        SeedSyncLogs(fixture, userId, count: 3);
        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        meta.LogRetentionFloor = 2;
        await fixture.Db.SaveChangesAsync();

        var clientSync = new ClientSyncStateService();
        var deleted = await SyncLogCompactor.CompactUserAsync(
            fixture.Db, clientSync, userId, cutoff: 1);

        Assert.Equal(0, deleted);
        meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        Assert.Equal(2, meta.LogRetentionFloor);
        Assert.Equal(3, await fixture.Db.SyncLogs.CountAsync(s => s.UserId == userId));
    }

    /// <summary>抬高 floor 失败时回滚已执行的删除，避免静默缺口</summary>
    [Fact]
    public async Task CompactUser_WhenFloorUpdateFails_RollsBackDeletes()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, _, _) = fixture.SeedUserWithNote();

        SeedSyncLogs(fixture, userId, count: 8);
        fixture.Db.ClientSyncStates.Add(new ClientSyncState
        {
            ApiKeyId = apiKeyId,
            UserId = userId,
            LastSyncVersion = 4,
            LastSeenAt = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var beforeCount = await fixture.Db.SyncLogs.CountAsync(s => s.UserId == userId);
        var clientSync = new FailingRetentionFloorClientSync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SyncLogCompactor.CompactUserAsync(fixture.Db, clientSync, userId, cutoff: 4));

        await using var verify = fixture.CreateSiblingContext();
        Assert.Equal(beforeCount, await verify.SyncLogs.CountAsync(s => s.UserId == userId));
        var meta = await verify.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        Assert.Equal(0, meta.LogRetentionFloor);
    }

    /// <summary>写入 Version=1..count 的 SyncLog</summary>
    private static void SeedSyncLogs(TestDbFactory fixture, Guid userId, int count)
    {
        for (var v = 1; v <= count; v++)
        {
            fixture.Db.SyncLogs.Add(new SyncLog
            {
                UserId = userId,
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Update,
                Version = v,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>模拟 UpdateRetentionFloor 失败</summary>
    private sealed class FailingRetentionFloorClientSync : ClientSyncStateService
    {
        /// <inheritdoc />
        public override Task UpdateRetentionFloorAsync(
            AppDbContext db,
            Guid userId,
            long cutoff,
            CancellationToken ct = default)
        {
            throw new InvalidOperationException("模拟 floor 更新失败");
        }
    }
}
