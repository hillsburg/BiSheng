using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Tests.Fixtures;
using BiSheng.Latte.ViewModels;
using Xunit;

namespace BiSheng.Latte.Tests.Editor;

/// <summary>EditorViewModel：LoadNote / TryReload 空 DB 守卫与编辑会话保护</summary>
[Collection("WpfSta")]
public class EditorViewModelTests : IDisposable
{
    private readonly LatteTestDbFactory _fixture;

    public EditorViewModelTests()
    {
        _fixture = new LatteTestDbFactory();
    }

    public void Dispose() => _fixture.Dispose();

    /// <summary>DB 内容被同步清空、列表缓存非空 → 再次 LoadNote 时不被空 DB 覆盖</summary>
    [StaFact]
    public void LoadNote_EmptyDbContent_PreservesIncomingContent()
    {
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "cached-body"
        });
        _fixture.Db.SaveChanges();

        var editor = CreateEditor();
        editor.LoadNote(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "cached-body"
        });
        Assert.Equal("cached-body", editor.EditorContent);

        // 模拟远端同步把 DB 正文清空，NoteList 仍持有带内容的缓存对象
        _fixture.Db.Notes.Find(noteId)!.Content = string.Empty;
        _fixture.Db.SaveChanges();

        editor.LoadNote(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "cached-body"
        });

        Assert.Equal("cached-body", editor.EditorContent);
    }

    /// <summary>DB 空内容 + 编辑器非空 + 非编辑会话 → 不 ContentRestored</summary>
    [StaFact]
    public void TryReloadCurrentNoteFromDb_EmptyDbWithEditorContent_SkipsRestore()
    {
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "user-text"
        });
        _fixture.Db.SaveChanges();

        var editor = CreateEditor();
        var note = new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "user-text"
        };
        editor.LoadNote(note);

        _fixture.Db.Notes.Find(noteId)!.Content = string.Empty;
        _fixture.Db.SaveChanges();

        var restored = false;
        editor.ContentRestored += () => restored = true;

        var reloaded = editor.TryReloadCurrentNoteFromDb();

        Assert.False(reloaded);
        Assert.False(restored);
        Assert.Equal("user-text", editor.EditorContent);
    }

    /// <summary>编辑会话活跃时 → TryReload 返回 false</summary>
    [StaFact]
    public void TryReloadCurrentNoteFromDb_EditingSessionActive_ReturnsFalse()
    {
        var noteId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _fixture.Db.Folders.Add(new LocalFolder { Id = folderId, Name = "F" });
        _fixture.Db.Notes.Add(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "db"
        });
        _fixture.Db.SaveChanges();

        var editor = CreateEditor();
        editor.LoadNote(new LocalNote
        {
            Id = noteId,
            FolderId = folderId,
            Title = "T",
            Content = "db"
        });
        editor.SetEditorFocus(true);

        Assert.True(editor.IsEditingSessionActive);
        Assert.False(editor.TryReloadCurrentNoteFromDb());
    }

    private static EditorViewModel CreateEditor()
    {
        var auth = new AuthService();
        var dbFactory = () => new LocalDbContext();
        var tracker = new LocalChangeTracker(dbFactory);
        var revisions = new NoteRevisionService(dbFactory, new ApiClient(auth), auth);
        return new EditorViewModel(tracker, revisions, dbFactory);
    }
}
