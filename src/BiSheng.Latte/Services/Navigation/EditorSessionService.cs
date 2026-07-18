using BiSheng.Latte.Data;
using BiSheng.Latte.ViewModels;
using BiSheng.Shared;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>编辑器与远端 DB 之间的会话边界：版本感知、避免无意义 reload</summary>
public sealed class EditorSessionService : IEditorSessionService
{
    private readonly EditorViewModel _editor;
    private readonly Func<LocalDbContext> _dbFactory;
    private Guid? _openNoteId;
    private long _loadedVersion;

    /// <summary>构造编辑器会话服务</summary>
    public EditorSessionService(EditorViewModel editor, Func<LocalDbContext> dbFactory)
    {
        _editor = editor;
        _dbFactory = dbFactory;
    }

    /// <inheritdoc />
    public void NotifyNoteOpened(Guid noteId, long loadedVersion)
    {
        _openNoteId = noteId;
        _loadedVersion = loadedVersion;
    }

    /// <inheritdoc />
    public void NotifyNoteClosed()
    {
        _openNoteId = null;
        _loadedVersion = 0;
    }

    /// <inheritdoc />
    public void ApplyRemoteChanges(SyncNavigationDelta delta)
    {
        if (_editor.CurrentNote == null || _openNoteId == null)
        {
            return;
        }

        if (_editor.IsEditingSessionActive)
        {
            return;
        }

        if (delta.RequiresFullRefresh)
        {
            TrySyncOpenNoteFromDb(force: true);
            return;
        }

        if (delta.Changes.Count == 0)
        {
            return;
        }

        var noteId = _openNoteId.Value;
        var affected = delta.Changes.Any(c =>
            c.EntityType == EntityTypes.Note && c.EntityId == noteId);

        if (!affected)
        {
            return;
        }

        TrySyncOpenNoteFromDb(force: false);
    }

    private void TrySyncOpenNoteFromDb(bool force)
    {
        if (_editor.CurrentNote == null || _openNoteId == null)
        {
            return;
        }

        using var db = _dbFactory();
        var fromDb = db.Notes.Find(_openNoteId.Value);
        if (fromDb == null || fromDb.IsDeleted)
        {
            _editor.ClearNote();
            NotifyNoteClosed();
            return;
        }

        if (!force && fromDb.Version <= _loadedVersion)
        {
            return;
        }

        _editor.TryReloadCurrentNoteFromDb();
        SyncLoadedVersionFromDb();
    }

    private void SyncLoadedVersionFromDb()
    {
        if (_openNoteId == null)
        {
            return;
        }

        using var db = _dbFactory();
        var fromDb = db.Notes.Find(_openNoteId.Value);
        if (fromDb != null)
        {
            _loadedVersion = fromDb.Version;
        }
    }
}
