using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>
/// A1：GET /api/sync/version 只刷新 LastSeenAt，不推进 LastSyncVersion
/// </summary>
public class GetServerVersionTests
{
    /// <summary>
    /// 探测版本后，ClientSyncState.LastSyncVersion 必须保持不变，仅 LastSeenAt 更新
    /// </summary>
    [Fact]
    public async Task GetServerVersion_DoesNotAdvanceLastSyncVersion_OnlyTouchesLastSeen()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, _, _) = fixture.SeedUserWithNote();

        // 模拟服务端已有更多变更：CurrentVersion 抬到 20
        fixture.Db.UserSyncMetas.Single().CurrentVersion = 20;
        var oldLastSeen = DateTime.UtcNow.AddDays(-1);
        fixture.Db.ClientSyncStates.Add(new ClientSyncState
        {
            ApiKeyId = apiKeyId,
            UserId = userId,
            LastSyncVersion = 5,
            LastSeenAt = oldLastSeen
        });
        fixture.Db.SaveChanges();

        var sync = SyncServiceFactory.New(fixture.Db);
        var v = await sync.GetServerVersionAsync(userId, apiKeyId, CancellationToken.None);

        Assert.Equal(20, v);

        var state = await fixture.Db.ClientSyncStates
            .SingleAsync(s => s.ApiKeyId == apiKeyId, CancellationToken.None);
        Assert.Equal(5, state.LastSyncVersion);               // 未推进
        Assert.True(state.LastSeenAt > oldLastSeen);          // 仅刷新 LastSeenAt
    }
}
