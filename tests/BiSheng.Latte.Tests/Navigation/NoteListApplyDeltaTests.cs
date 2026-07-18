using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Latte.ViewModels;
using BiSheng.Shared;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>NoteListViewModel.ApplyNavigationDelta：并列模式增量 patch</summary>
[Collection("WpfSta")]
public class NoteListApplyDeltaTests : IDisposable
{
    private readonly LatteTestDbFactory _fixture;

    public NoteListApplyDeltaTests()
    {
        _fixture = new LatteTestDbFactory();
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>远端新建 Note 应插入当前文件夹列表</summary>
    [StaFact]
    public void ApplyDelta_CreateNote_InsertsIntoCurrentFolder()
    {
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.SaveChanges();

        var tracker = new LocalChangeTracker(() => new LocalDbContext());
        var filterState = NavigationTestPublisher.CreateFilterState();
        var (_, publisher) = NavigationTestPublisher.Create();
        var noteList = new NoteListViewModel(tracker, () => new LocalDbContext(), publisher, filterState);
        noteList.LoadNotes(folderId);

        Assert.Empty(noteList.Notes);

        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "New",
            Content = "body"
        });
        _fixture.Db.SaveChanges();

        var ok = noteList.ApplyNavigationDelta(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Note,
                EntityId = noteId,
                Action = ChangeActions.Create,
                FolderId = folderId
            }
        });

        Assert.True(ok);
        Assert.Single(noteList.Notes);
        Assert.Equal(noteId, noteList.Notes[0].Id);
        Assert.Equal("New", noteList.Notes[0].Title);
    }

    /// <summary>远端删除 Note 应从当前列表移除</summary>
    [StaFact]
    public void ApplyDelta_DeleteNote_RemovesFromList()
    {
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "Gone",
            Content = "body"
        });
        _fixture.Db.SaveChanges();

        var tracker = new LocalChangeTracker(() => new LocalDbContext());
        var filterState = NavigationTestPublisher.CreateFilterState();
        var (_, publisher) = NavigationTestPublisher.Create();
        var noteList = new NoteListViewModel(tracker, () => new LocalDbContext(), publisher, filterState);
        noteList.LoadNotes(folderId);
        Assert.Single(noteList.Notes);

        var note = _fixture.Db.Notes.Find(noteId)!;
        note.IsDeleted = true;
        _fixture.Db.SaveChanges();

        var ok = noteList.ApplyNavigationDelta(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Note,
                EntityId = noteId,
                Action = ChangeActions.Delete,
                FolderId = folderId
            }
        });

        Assert.True(ok);
        Assert.Empty(noteList.Notes);
    }

    /// <summary>Note 移出当前文件夹时应从列表移除</summary>
    [StaFact]
    public void ApplyDelta_MoveOutOfFolder_RemovesFromList()
    {
        var folderA = Guid.NewGuid();
        var folderB = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderA, Name = "A" });
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderB, Name = "B" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderA,
            Title = "Move",
            Content = "body"
        });
        _fixture.Db.SaveChanges();

        var tracker = new LocalChangeTracker(() => new LocalDbContext());
        var filterState = NavigationTestPublisher.CreateFilterState();
        var (_, publisher) = NavigationTestPublisher.Create();
        var noteList = new NoteListViewModel(tracker, () => new LocalDbContext(), publisher, filterState);
        noteList.LoadNotes(folderA);
        Assert.Single(noteList.Notes);

        var note = _fixture.Db.Notes.Find(noteId)!;
        note.FolderId = folderB;
        _fixture.Db.SaveChanges();

        var ok = noteList.ApplyNavigationDelta(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Note,
                EntityId = noteId,
                Action = ChangeActions.Update,
                FolderId = folderB
            }
        });

        Assert.True(ok);
        Assert.Empty(noteList.Notes);
    }
}
