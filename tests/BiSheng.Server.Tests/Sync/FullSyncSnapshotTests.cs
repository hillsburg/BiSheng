using BiSheng.Server.Data.Entities;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>RequiresFullSync 后 since=0 实体快照可重建（压缩后不再死循环）</summary>
public class FullSyncSnapshotTests
{
    /// <summary>游标落在裁剪线以下时返回 RequiresFullSync，且不发变更</summary>
    [Fact]
    public async Task Pull_WhenBehindRetentionFloor_RequiresFullSync()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, _, _) = fixture.SeedUserWithNote();

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        meta.LogRetentionFloor = 50;
        meta.CurrentVersion = 100;
        await fixture.Db.SaveChangesAsync();

        var sync = SyncServiceFactory.New(fixture.Db);
        var page = await sync.PullAsync(userId, apiKeyId, since: 10, limit: 50);

        Assert.True(page.RequiresFullSync);
        Assert.Empty(page.Changes);
        Assert.False(page.IsEntitySnapshot);
        Assert.Equal(100, page.ServerVersion);
    }

    /// <summary>since=0 无视裁剪线，分页导出文件夹+笔记并推进设备游标到 tip</summary>
    [Fact]
    public async Task Pull_SinceZero_ExportsEntitySnapshot_IgnoringFloor()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, noteId) = fixture.SeedUserWithNote("body");

        // 再造一个软删笔记，确保快照含 Delete
        var deletedNoteId = Guid.NewGuid();
        fixture.Db.Notes.Add(new Note
        {
            Id = deletedNoteId,
            UserId = userId,
            FolderId = folderId,
            Title = "gone",
            Content = "x",
            Version = 20,
            IsDeleted = true,
            UpdatedAt = DateTime.UtcNow
        });

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        meta.LogRetentionFloor = 50;
        meta.CurrentVersion = 100;
        // 故意不写 SyncLog，验证完全走实体表
        await fixture.Db.SaveChangesAsync();

        var sync = SyncServiceFactory.New(fixture.Db);

        var pulled = new List<ChangeDto>();
        long offset = 0;
        SyncPullResponse? last = null;
        for (var i = 0; i < 10; i++)
        {
            var page = await sync.PullAsync(
                userId, apiKeyId, since: 0, limit: 2, snapshotOffset: offset);
            Assert.False(page.RequiresFullSync);
            Assert.True(page.IsEntitySnapshot);
            Assert.Equal(100, page.ServerVersion);
            pulled.AddRange(page.Changes);
            last = page;
            if (!page.HasMore)
            {
                break;
            }

            Assert.True(page.NextSince > offset);
            offset = page.NextSince;
        }

        Assert.NotNull(last);
        Assert.False(last!.HasMore);
        Assert.Equal(100, last.NextSince);

        Assert.Equal(3, pulled.Count);
        Assert.Contains(pulled, c => c.EntityId == folderId && c.Action == ChangeActions.Update);
        Assert.Contains(pulled, c => c.EntityId == noteId && c.Action == ChangeActions.Update && c.Version == 10);
        Assert.Contains(pulled, c => c.EntityId == deletedNoteId && c.Action == ChangeActions.Delete);

        var finalCursor = await fixture.Db.ClientSyncStates
            .Where(c => c.ApiKeyId == apiKeyId)
            .Select(c => c.LastSyncVersion)
            .SingleAsync();
        Assert.Equal(100, finalCursor);
    }

    /// <summary>
    /// 跨文件夹/笔记边界分页：批量 Skip/Take 路径与旧 offset 语义一致
    /// </summary>
    [Fact]
    public async Task Pull_SinceZero_PaginatesAcrossFolderNoteBoundary()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, folderId, noteId) = fixture.SeedUserWithNote("body");

        var folder2Id = Guid.NewGuid();
        fixture.Db.Folders.Add(new Folder
        {
            Id = folder2Id,
            UserId = userId,
            Name = "F2",
            Version = 2,
            UpdatedAt = DateTime.UtcNow
        });
        var note2Id = Guid.NewGuid();
        fixture.Db.Notes.Add(new Note
        {
            Id = note2Id,
            UserId = userId,
            FolderId = folderId,
            Title = "N2",
            Content = "y",
            Version = 3,
            UpdatedAt = DateTime.UtcNow
        });

        var meta = await fixture.Db.UserSyncMetas.SingleAsync(m => m.UserId == userId);
        meta.CurrentVersion = 30;
        await fixture.Db.SaveChangesAsync();

        var sync = SyncServiceFactory.New(fixture.Db);
        var pulled = new List<ChangeDto>();
        long offset = 0;
        for (var i = 0; i < 10; i++)
        {
            var page = await sync.PullAsync(
                userId, apiKeyId, since: 0, limit: 1, snapshotOffset: offset);
            Assert.True(page.IsEntitySnapshot);
            Assert.True(page.Changes.Count <= 1);
            pulled.AddRange(page.Changes);
            if (!page.HasMore)
            {
                Assert.Equal(30, page.NextSince);
                break;
            }

            offset = page.NextSince;
        }

        // 2 folders + 2 notes
        Assert.Equal(4, pulled.Count);
        Assert.Equal(2, pulled.Count(c => c.EntityType == EntityTypes.Folder));
        Assert.Equal(2, pulled.Count(c => c.EntityType == EntityTypes.Note));
        Assert.Contains(pulled, c => c.EntityId == folderId);
        Assert.Contains(pulled, c => c.EntityId == folder2Id);
        Assert.Contains(pulled, c => c.EntityId == noteId);
        Assert.Contains(pulled, c => c.EntityId == note2Id);

        // 文件夹页必须全部出现在笔记页之前
        var firstNoteIndex = pulled.FindIndex(c => c.EntityType == EntityTypes.Note);
        Assert.True(firstNoteIndex >= 0);
        Assert.All(pulled.Take(firstNoteIndex), c => Assert.Equal(EntityTypes.Folder, c.EntityType));
    }
}
