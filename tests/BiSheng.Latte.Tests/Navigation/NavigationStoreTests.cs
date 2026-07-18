using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Latte.ViewModels;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>LocalNoteMerger 与 SelectNoteById：单一读模型合并规则</summary>
[Collection("WpfSta")]
public class NavigationStoreTests : IDisposable
{
    private readonly LatteTestDbFactory _fixture;

    public NavigationStoreTests()
    {
        _fixture = new LatteTestDbFactory();
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>DB 内容为空时不覆盖 target 非空正文</summary>
    [Fact]
    public void MergeFields_EmptyDbContent_DoesNotOverwriteTarget()
    {
        var target = new LocalNote { Content = "editor-body" };
        var source = new LocalNote { Content = "" };

        LocalNoteMerger.MergeFields(target, source);

        Assert.Equal("editor-body", target.Content);
    }

    /// <summary>SelectNoteById 从 Notes 集合解析，不接受树节点外部引用</summary>
    [StaFact]
    public void SelectNoteById_ResolvesFromNotesCollection()
    {
        var folderId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "N",
            Content = "body"
        });
        _fixture.Db.SaveChanges();

        var tracker = new LocalChangeTracker(() => new LocalDbContext());
        var dbFactory = () => new LocalDbContext();
        var auth = new AuthService();
        var filterState = NavigationTestPublisher.CreateFilterState();
        var (_, publisher) = NavigationTestPublisher.Create();
        var noteList = new NoteListViewModel(tracker, dbFactory, publisher, filterState);
        var store = new NavigationStore(
            new FolderTreeViewModel(tracker, dbFactory, publisher, filterState),
            noteList,
            new EditorViewModel(tracker, new NoteRevisionService(dbFactory, new ApiClient(auth), auth), dbFactory),
            filterState);

        var treeItem = new NoteItemViewModel(_fixture.Db.Notes.Find(noteId)!);

        Assert.True(store.SelectNoteById(noteId, folderId));
        Assert.Same(noteList.Notes.First(n => n.Id == noteId), noteList.SelectedNote);
        Assert.NotSame(treeItem, noteList.SelectedNote);
    }
}
