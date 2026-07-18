using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services.Mutations;
using BiSheng.Server.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Tests.Mutations;

/// <summary>
/// PR-1：EntityChangeWriter 与 SyncService.ApplyClientChange 行为对齐（Writer 为后续唯一写入口）
/// </summary>
public class EntityChangeWriterTests
{
    /// <summary>在有效 folder 下 Create Note 应落库、写 SyncLog、递增版本</summary>
    [Fact]
    public async Task TryApply_CreateNote_SucceedsWithSyncLog()
    {
        using var fixture = new TestDbFactory();
        var (userId, _, folderId, _) = fixture.SeedUserWithNote();
        var noteId = Guid.NewGuid();
        var batch = await EntityChangeWriterTestHelper.CreateBatchContextAsync(fixture.Db, userId);

        var result = await EntityChangeWriterTestHelper.ApplyAndSaveAsync(
            fixture.Db,
            userId,
            new EntityMutation
            {
                EntityType = EntityTypes.Note,
                EntityId = noteId,
                Action = ChangeActions.Create,
                Payload = $$"""{"title":"N2","content":"hello","folderId":"{{folderId}}","isFavorite":false,"isPinned":false}""",
                UpdatedAt = DateTime.UtcNow
            },
            batch);

        var applied = Assert.IsType<MutationApplied>(result);
        Assert.Equal(11, applied.Applied.Version);

        var note = await fixture.Db.Notes.FindAsync(noteId);
        Assert.NotNull(note);
        Assert.Equal("hello", note!.Content);
        Assert.Equal(11, note.Version);

        var log = await fixture.Db.SyncLogs
            .SingleAsync(s => s.EntityId == noteId && s.Action == ChangeActions.Create);
        Assert.Equal(11, log.Version);
    }

    /// <summary>folderId 无效时应 Skipped，不消耗版本</summary>
    [Fact]
    public async Task TryApply_CreateNote_InvalidFolder_SkippedNoVersionConsumed()
    {
        using var fixture = new TestDbFactory();
        var (userId, _, _, _) = fixture.SeedUserWithNote();
        var batch = await EntityChangeWriterTestHelper.CreateBatchContextAsync(fixture.Db, userId);
        var ghostFolder = Guid.NewGuid();

        var result = await EntityChangeWriterTestHelper.ApplyAndSaveAsync(
            fixture.Db,
            userId,
            new EntityMutation
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Create,
                Payload = $$"""{"title":"X","content":"x","folderId":"{{ghostFolder}}","isFavorite":false,"isPinned":false}"""
            },
            batch);

        Assert.IsType<MutationSkipped>(result);
        Assert.Equal(10, await fixture.Db.UserSyncMetas
            .Where(m => m.UserId == userId)
            .Select(m => m.CurrentVersion)
            .SingleAsync());
    }

    /// <summary>Delete Folder 应级联软删子孙 folder + note，与 FolderCascadeDeleteTests 一致</summary>
    [Fact]
    public async Task TryApply_DeleteFolder_CascadesToDescendants()
    {
        using var fixture = new TestDbFactory();
        var (userId, _, _, _) = fixture.SeedUserWithNote();

        var f1 = Guid.NewGuid();
        var f2 = Guid.NewGuid();
        var n1 = Guid.NewGuid();

        fixture.Db.Folders.Add(new Folder { Id = f1, UserId = userId, Name = "F1", Version = 10 });
        fixture.Db.Folders.Add(new Folder { Id = f2, UserId = userId, Name = "F2", ParentId = f1, Version = 10 });
        fixture.Db.Notes.Add(new Note
        {
            Id = n1,
            UserId = userId,
            FolderId = f2,
            Title = "N1",
            Content = "c",
            Version = 10
        });
        await fixture.Db.SaveChangesAsync();

        var batch = await EntityChangeWriterTestHelper.CreateBatchContextAsync(fixture.Db, userId);
        var result = await EntityChangeWriterTestHelper.ApplyAndSaveAsync(
            fixture.Db,
            userId,
            new EntityMutation
            {
                EntityType = EntityTypes.Folder,
                EntityId = f1,
                Action = ChangeActions.Delete,
                UpdatedAt = DateTime.UtcNow
            },
            batch);

        var applied = Assert.IsType<MutationApplied>(result);
        Assert.Equal(2, applied.Applied.Cascaded.Count);

        Assert.True((await fixture.Db.Folders.FindAsync(f1))!.IsDeleted);
        Assert.True((await fixture.Db.Folders.FindAsync(f2))!.IsDeleted);
        Assert.True((await fixture.Db.Notes.FindAsync(n1))!.IsDeleted);

        var deleteLogs = await fixture.Db.SyncLogs
            .Where(s => s.UserId == userId && s.Action == ChangeActions.Delete)
            .ToListAsync();
        Assert.Equal(3, deleteLogs.Count);
    }

    /// <summary>ClientChangeDto.ToEntityMutation 字段映射正确</summary>
    [Fact]
    public void ToEntityMutation_MapsAllFields()
    {
        var id = Guid.NewGuid();
        var at = DateTime.UtcNow;
        var dto = new ClientChangeDto
        {
            EntityType = EntityTypes.Note,
            EntityId = id,
            Action = ChangeActions.Update,
            Payload = "{}",
            UpdatedAt = at
        };

        var mutation = dto.ToEntityMutation();
        Assert.Equal(dto.EntityType, mutation.EntityType);
        Assert.Equal(id, mutation.EntityId);
        Assert.Equal(dto.Action, mutation.Action);
        Assert.Equal("{}", mutation.Payload);
        Assert.Equal(at, mutation.UpdatedAt);
    }
}
