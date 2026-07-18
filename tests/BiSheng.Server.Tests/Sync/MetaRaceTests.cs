using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>
/// L：UserSyncMeta 初始化竞态与回填
/// </summary>
public class MetaRaceTests
{
    /// <summary>
    /// 用户已有 SyncLog 但无 UserSyncMeta 时，EnsureInitializedAsync 应从历史 SyncLog
    /// 回填 CurrentVersion，且多次调用幂等不抛 PK 冲突
    /// </summary>
    [Fact]
    public async Task EnsureInitialized_BackfillsFromSyncLogs_AndIsIdempotent()
    {
        using var fixture = new TestDbFactory();
        var userId = Guid.NewGuid();
        var apiKeyId = Guid.NewGuid();

        // 种子：用户 + ApiKey + 两条 SyncLog（v5、v8），但故意不建 UserSyncMeta
        fixture.Db.Users.Add(new User
        {
            Id = userId,
            Username = "u",
            PasswordHash = "x",
            TotpSecret = "x"
        });
        fixture.Db.ApiKeys.Add(new ApiKey { Id = apiKeyId, UserId = userId, KeyValue = "k" });
        fixture.Db.SyncLogs.Add(new SyncLog
        {
            EntityType = EntityTypes.Note,
            EntityId = Guid.NewGuid(),
            Action = ChangeActions.Create,
            Version = 5,
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
        fixture.Db.SyncLogs.Add(new SyncLog
        {
            EntityType = EntityTypes.Note,
            EntityId = Guid.NewGuid(),
            Action = ChangeActions.Create,
            Version = 8,
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
        await fixture.Db.SaveChangesAsync();

        var versionService = new UserSyncVersionService();

        // 第一次：应插入 UserSyncMeta 且 CurrentVersion 回填到 8
        await versionService.EnsureInitializedAsync(fixture.Db, userId);
        await fixture.Db.SaveChangesAsync();

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        Assert.Equal(8, meta.CurrentVersion);

        // 第二次：幂等，不抛 PK 冲突，CurrentVersion 不变
        await versionService.EnsureInitializedAsync(fixture.Db, userId);
        await fixture.Db.SaveChangesAsync();

        var metas = await fixture.Db.UserSyncMetas.Where(m => m.UserId == userId).ToListAsync();
        Assert.Single(metas);
        Assert.Equal(8, metas[0].CurrentVersion);
    }
}
