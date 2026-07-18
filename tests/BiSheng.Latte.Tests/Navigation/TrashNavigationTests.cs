using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>TrashService 恢复/永久删除时发布导航增量</summary>
public class TrashNavigationTests : IDisposable
{
    private readonly LatteTestDbFactory _fixture;

    public TrashNavigationTests()
    {
        _fixture = new LatteTestDbFactory();
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>恢复软删笔记应发布 Create delta</summary>
    [Fact]
    public void Restore_DeletedNote_PublishesNoteCreated()
    {
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "Trashed",
            Content = "body",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        _fixture.Db.SaveChanges();

        var readModel = new NavigationReadModel();
        var publisher = new NavigationMutationPublisher(readModel);
        NavigationProjectionUpdate? captured = null;
        readModel.Changed += update => captured = update;

        var trash = new TrashService(
            new LocalChangeTracker(() => new LocalDbContext()),
            () => new LocalDbContext(),
            publisher);

        trash.Restore(EntityTypes.Note, noteId);

        Assert.NotNull(captured);
        Assert.Equal(NavigationProjectionKind.DataChange, captured!.Kind);
        var change = Assert.Single(captured.Delta!.Changes);
        Assert.Equal(EntityTypes.Note, change.EntityType);
        Assert.Equal(noteId, change.EntityId);
        Assert.Equal(ChangeActions.Create, change.Action);
        Assert.Equal(folderId, change.FolderId);
    }

    /// <summary>永久删除仍在导航中的项应发布 Delete delta</summary>
    [Fact]
    public void PurgePermanently_VisibleNote_PublishesDelete()
    {
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "Live",
            Content = "body",
            IsDeleted = false
        });
        _fixture.Db.SaveChanges();

        var readModel = new NavigationReadModel();
        var publisher = new NavigationMutationPublisher(readModel);
        NavigationProjectionUpdate? captured = null;
        readModel.Changed += update => captured = update;

        var trash = new TrashService(
            new LocalChangeTracker(() => new LocalDbContext()),
            () => new LocalDbContext(),
            publisher);

        trash.PurgePermanently(EntityTypes.Note, noteId);

        Assert.NotNull(captured);
        Assert.Equal(NavigationProjectionKind.DataChange, captured!.Kind);
        var change = Assert.Single(captured.Delta!.Changes);
        Assert.Equal(ChangeActions.Delete, change.Action);
        Assert.Equal(noteId, change.EntityId);
    }

    /// <summary>永久删除已在回收站的项不发布导航变更</summary>
    [Fact]
    public void PurgePermanently_SoftDeletedNote_DoesNotPublish()
    {
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "Trashed",
            Content = "body",
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow
        });
        _fixture.Db.SaveChanges();

        var readModel = new NavigationReadModel();
        var publisher = new NavigationMutationPublisher(readModel);
        var published = false;
        readModel.Changed += _ => published = true;

        var trash = new TrashService(
            new LocalChangeTracker(() => new LocalDbContext()),
            () => new LocalDbContext(),
            publisher);

        trash.PurgePermanently(EntityTypes.Note, noteId);

        Assert.False(published);
    }
}
