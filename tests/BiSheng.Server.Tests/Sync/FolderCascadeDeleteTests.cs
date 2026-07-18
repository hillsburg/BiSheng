using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Sync;

/// <summary>
/// F：删除 folder 时级联软删子孙 folder + 直属 note，各分配版本 + SyncLog
/// </summary>
public class FolderCascadeDeleteTests
{
    /// <summary>
    /// 结构：F1 ← F2(子) ← F3(孙)；N1 在 F1，N2 在 F2，N3 在 F3。
    /// Push Delete F1 后，F1/F2/F3/N1/N2/N3 全部 IsDeleted=true，
    /// 各有一条 Delete SyncLog，版本号连续递增
    /// </summary>
    [Fact]
    public async Task Push_DeleteFolder_CascadesToAllDescendants()
    {
        using var fixture = new TestDbFactory();
        var (userId, apiKeyId, _, _) = fixture.SeedUserWithNote();

        // 构造 F1 ← F2 ← F3 三层 folder
        var f1 = Guid.NewGuid();
        var f2 = Guid.NewGuid();
        var f3 = Guid.NewGuid();
        var n1 = Guid.NewGuid();
        var n2 = Guid.NewGuid();
        var n3 = Guid.NewGuid();

        fixture.Db.Folders.Add(new Folder { Id = f1, UserId = userId, Name = "F1", Version = 10 });
        fixture.Db.Folders.Add(new Folder { Id = f2, UserId = userId, Name = "F2", ParentId = f1, Version = 10 });
        fixture.Db.Folders.Add(new Folder { Id = f3, UserId = userId, Name = "F3", ParentId = f2, Version = 10 });
        fixture.Db.Notes.Add(new Note { Id = n1, UserId = userId, FolderId = f1, Title = "N1", Content = "c", Version = 10 });
        fixture.Db.Notes.Add(new Note { Id = n2, UserId = userId, FolderId = f2, Title = "N2", Content = "c", Version = 10 });
        fixture.Db.Notes.Add(new Note { Id = n3, UserId = userId, FolderId = f3, Title = "N3", Content = "c", Version = 10 });
        await fixture.Db.SaveChangesAsync();

        var sync = SyncServiceFactory.New(fixture.Db);

        var resp = await sync.PushAsync(userId, apiKeyId, new SyncPushRequest
        {
            ClientVersion = 10,
            Changes = new()
            {
                new ClientChangeDto
                {
                    EntityType = EntityTypes.Folder,
                    EntityId = f1,
                    Action = ChangeActions.Delete,
                    UpdatedAt = DateTime.UtcNow
                }
            }
        }, CancellationToken.None);

        Assert.True(resp.Success);
        Assert.Empty(resp.FailedEntityIds);

        // 所有 folder + note 都软删
        Assert.True((await fixture.Db.Folders.FindAsync(f1))!.IsDeleted);
        Assert.True((await fixture.Db.Folders.FindAsync(f2))!.IsDeleted);
        Assert.True((await fixture.Db.Folders.FindAsync(f3))!.IsDeleted);
        Assert.True((await fixture.Db.Notes.FindAsync(n1))!.IsDeleted);
        Assert.True((await fixture.Db.Notes.FindAsync(n2))!.IsDeleted);
        Assert.True((await fixture.Db.Notes.FindAsync(n3))!.IsDeleted);

        // 6 个实体各有一条 Delete SyncLog（F1 根 + F2/F3 子孙 folder + N1/N2/N3 note）
        var deleteLogs = await fixture.Db.SyncLogs
            .Where(s => s.UserId == userId && s.Action == ChangeActions.Delete)
            .ToListAsync();
        Assert.Equal(6, deleteLogs.Count);

        // 版本号连续递增 11..16
        var versions = deleteLogs.Select(s => s.Version).OrderBy(v => v).ToList();
        Assert.Equal(new long[] { 11, 12, 13, 14, 15, 16 }, versions);
    }
}
