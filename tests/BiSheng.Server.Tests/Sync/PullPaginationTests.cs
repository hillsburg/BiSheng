using BiSheng.Server.Data.Entities;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>PR4：Pull 按 SyncLog 版本分页 + 批内终态折叠</summary>
public class PullPaginationTests
{
    /// <summary>limit=2 时分批返回，HasMore/NextSince 正确，最终追到 tip</summary>
    [Fact]
    public async Task Pull_WithLimit_PagesUntilCaughtUp()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, _) = fixture.SeedUserWithNote();

        // 再造 4 篇笔记 + SyncLog v11..v15（种子 CurrentVersion=10）
        var noteIds = new List<Guid>();
        for (var v = 11; v <= 15; v++)
        {
            var noteId = Guid.NewGuid();
            noteIds.Add(noteId);
            fixture.Db.Notes.Add(new Note
            {
                Id = noteId,
                UserId = userId,
                FolderId = folderId,
                Title = $"N{v}",
                Content = $"c{v}",
                Version = v
            });
            fixture.Db.SyncLogs.Add(new SyncLog
            {
                EntityType = EntityTypes.Note,
                EntityId = noteId,
                Action = ChangeActions.Create,
                Version = v,
                UserId = userId,
                Timestamp = DateTime.UtcNow,
                Payload = SyncPayloadJson.Serialize(
                    SyncPayloadBuilder.Note($"N{v}", $"c{v}", folderId))
            });
        }

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        meta.CurrentVersion = 15;
        await fixture.Db.SaveChangesAsync();

        var sync = SyncServiceFactory.New(fixture.Db);

        // 第 1 批：v11,v12
        var page1 = await sync.PullAsync(userId, apiKeyId, since: 10, limit: 2);
        Assert.False(page1.RequiresFullSync);
        Assert.True(page1.HasMore);
        Assert.Equal(15, page1.ServerVersion);
        Assert.Equal(12, page1.NextSince);
        Assert.Equal(2, page1.Changes.Count);
        Assert.Equal(12, await GetDeviceCursorAsync(fixture, apiKeyId));

        // 第 2 批：v13,v14
        var page2 = await sync.PullAsync(userId, apiKeyId, since: page1.NextSince, limit: 2);
        Assert.True(page2.HasMore);
        Assert.Equal(14, page2.NextSince);
        Assert.Equal(2, page2.Changes.Count);

        // 第 3 批：v15
        var page3 = await sync.PullAsync(userId, apiKeyId, since: page2.NextSince, limit: 2);
        Assert.False(page3.HasMore);
        Assert.Equal(15, page3.NextSince);
        Assert.Equal(15, page3.ServerVersion);
        Assert.Single(page3.Changes);
        Assert.Equal(15, await GetDeviceCursorAsync(fixture, apiKeyId));

        var pulledIds = page1.Changes.Concat(page2.Changes).Concat(page3.Changes)
            .Select(c => c.EntityId)
            .ToHashSet();
        Assert.Equal(noteIds.ToHashSet(), pulledIds);
    }

    /// <summary>同一实体在一批内多次变更时只返回终态一条</summary>
    [Fact]
    public async Task Pull_FoldsEntityWithinPage()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, noteId) = fixture.SeedUserWithNote("base");

        // 同笔记 v11 Update、v12 Update；limit 足够装下两行
        fixture.Db.SyncLogs.Add(new SyncLog
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Version = 11,
            UserId = userId,
            Timestamp = DateTime.UtcNow.AddMinutes(-1),
            Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.Note("old", "old", folderId))
        });
        fixture.Db.SyncLogs.Add(new SyncLog
        {
            EntityType = EntityTypes.Note,
            EntityId = noteId,
            Action = ChangeActions.Update,
            Version = 12,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.Note("new", "new", folderId))
        });

        var note = await fixture.Db.Notes.SingleAsync(n => n.Id == noteId);
        note.Title = "new";
        note.Content = "new";
        note.Version = 12;

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        meta.CurrentVersion = 12;
        await fixture.Db.SaveChangesAsync();

        var sync = SyncServiceFactory.New(fixture.Db);
        var page = await sync.PullAsync(userId, apiKeyId, since: 10, limit: 10);

        Assert.False(page.HasMore);
        Assert.Equal(12, page.NextSince);
        Assert.Single(page.Changes);
        Assert.Equal(noteId, page.Changes[0].EntityId);
        Assert.Contains("new", page.Changes[0].Payload);
        Assert.DoesNotContain("\"title\":\"old\"", page.Changes[0].Payload);
    }

    /// <summary>limit≤0 时使用默认页大小，仍分页返回</summary>
    [Fact]
    public async Task Pull_DefaultLimit_StillPages()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, _) = fixture.SeedUserWithNote();

        // 写入超过 DefaultPullLimit 的 SyncLog 不现实；用 limit=0 验证落到默认页大小逻辑：
        // 3 条日志 + limit=0 → 一页装下，HasMore=false，NextSince/ServerVersion 齐全
        for (var v = 11; v <= 13; v++)
        {
            var noteId = Guid.NewGuid();
            fixture.Db.Notes.Add(new Note
            {
                Id = noteId,
                UserId = userId,
                FolderId = folderId,
                Title = $"N{v}",
                Content = "x",
                Version = v
            });
            fixture.Db.SyncLogs.Add(new SyncLog
            {
                EntityType = EntityTypes.Note,
                EntityId = noteId,
                Action = ChangeActions.Create,
                Version = v,
                UserId = userId,
                Timestamp = DateTime.UtcNow,
                Payload = SyncPayloadJson.Serialize(SyncPayloadBuilder.Note($"N{v}", "x", folderId))
            });
        }

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        meta.CurrentVersion = 13;
        await fixture.Db.SaveChangesAsync();

        var sync = SyncServiceFactory.New(fixture.Db);
        var page = await sync.PullAsync(userId, apiKeyId, since: 10, limit: 0);

        Assert.False(page.HasMore);
        Assert.Equal(13, page.NextSince);
        Assert.Equal(13, page.ServerVersion);
        Assert.Equal(3, page.Changes.Count);
    }

    /// <summary>读取设备同步游标</summary>
    private static async Task<long> GetDeviceCursorAsync(TestDbFactory fixture, Guid apiKeyId)
    {
        var state = await fixture.Db.ClientSyncStates.SingleAsync(s => s.ApiKeyId == apiKeyId);
        return state.LastSyncVersion;
    }
}
