using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>
/// D：Push 事务回滚时返回真实 CurrentVersion + TransactionRolledBack 标志
/// </summary>
public class PushRollbackTests
{
    /// <summary>
    /// 种子一条 v11 SyncLog 但 CurrentVersion=10（不一致种子）；
    /// Push 成功项分配到 v11 时 SaveChanges 因唯一约束抛 → 外层 catch → rollback；
    /// 响应必须 TransactionRolledBack=true、ServerVersion=回滚后真实水位 10
    /// </summary>
    [Fact]
    public async Task Push_RolledBack_ReturnsRealCurrentVersionAndFlag()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, _) = fixture.SeedUserWithNote(); // CurrentVersion=10

        // 故意占用 v11：让 ReserveNextVersion 返回 11 后 SaveChanges 唯一约束冲突
        fixture.Db.SyncLogs.Add(new SyncLog
        {
            EntityType = EntityTypes.Note,
            EntityId = Guid.NewGuid(),
            Action = ChangeActions.Update,
            Version = 11,
            UserId = userId,
            Timestamp = DateTime.UtcNow
        });
        fixture.Db.SaveChanges();

        var sync = SyncServiceFactory.New(fixture.Db);

        var resp = await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes = new()
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Note,
                    EntityId = Guid.NewGuid(),
                    Action = ChangeActions.Create,
                    Payload = $$"""{"title":"X","content":"x","folderId":"{{folderId}}","isFavorite":false,"isPinned":false}"""
                }
            }
        }, CancellationToken.None);

        // 响应断言
        Assert.False(resp.Success);
        Assert.True(resp.TransactionRolledBack);
        Assert.Equal(10, resp.ServerVersion);   // 回滚后真实水位，不是 0

        // CurrentVersion 未前进（UPDATE … RETURNING 也在事务内，一并回滚）
        Assert.Equal(10, await fixture.Db.UserSyncMetas
            .Where(m => m.UserId == userId)
            .Select(m => m.CurrentVersion)
            .SingleAsync(CancellationToken.None));

        // 实体未落库
        var newNotes = fixture.Db.Notes.Where(n => n.Title == "X").ToList();
        Assert.Empty(newNotes);
    }
}
