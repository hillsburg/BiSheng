using System.Windows;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>
/// 导航 Store：以 note/folder Id 为权威，由展示协调器驱动增量 patch 或 fallback 全量 Refresh。
/// </summary>
public sealed class NavigationStore : INavigationStore
{
    private readonly FolderTreeViewModel _folderTree;
    private readonly NoteListViewModel _noteList;
    private readonly EditorViewModel _editor;
    private readonly INavigationFilterState _filterState;

    /// <inheritdoc />
    public Guid? SelectedFolderId =>
        _noteList.CurrentFolderId ?? _folderTree.SelectedFolder?.Id;

    /// <inheritdoc />
    public Guid? SelectedNoteId =>
        _noteList.SelectedNote?.Id ?? _editor.CurrentNote?.Id;

    /// <inheritdoc />
    public event Action<LocalNote>? NoteSwitching;

    /// <inheritdoc />
    public event Action? NoteClosed;

    /// <summary>构造导航 Store</summary>
    public NavigationStore(
        FolderTreeViewModel folderTree,
        NoteListViewModel noteList,
        EditorViewModel editor,
        INavigationFilterState filterState)
    {
        _folderTree = folderTree;
        _noteList = noteList;
        _editor = editor;
        _filterState = filterState;

        _noteList.OnNoteSelected += note =>
        {
            if (note == null)
            {
                _editor.ClearNote();
                NoteClosed?.Invoke();
                return;
            }

            if (_editor.CurrentNote?.Id == note.Id)
            {
                return;
            }

            NoteSwitching?.Invoke(note);
        };
    }

    /// <inheritdoc />
    public bool SelectNoteById(Guid noteId, Guid folderId)
    {
        if (_noteList.CurrentFolderId != folderId)
        {
            _noteList.LoadNotes(folderId);
        }

        var item = _noteList.Notes.FirstOrDefault(n => n.Id == noteId);
        if (item == null)
        {
            _noteList.Refresh();
            item = _noteList.Notes.FirstOrDefault(n => n.Id == noteId);
        }

        if (item == null)
        {
            return false;
        }

        _noteList.SelectedNote = item;
        return true;
    }

    /// <inheritdoc />
    public void Refresh(NavigationRefreshScope scope = NavigationRefreshScope.All)
    {
        switch (scope)
        {
            case NavigationRefreshScope.FolderTree:
                _folderTree.Refresh();
                break;

            case NavigationRefreshScope.NoteList:
                _noteList.Refresh();
                break;

            case NavigationRefreshScope.CurrentNoteOnly:
                _editor.TryReloadCurrentNoteFromDb();
                break;

            default:
                _folderTree.Refresh();
                _noteList.Refresh();
                break;
        }
    }

    /// <inheritdoc />
    public void ApplyRemoteDelta(SyncNavigationDelta delta, bool isTreeMode)
    {
        if (NavigationPatcher.TryApplyIncremental(
                delta,
                _folderTree,
                _noteList,
                _filterState,
                isTreeMode,
                _folderTree.IsInlineRenamingActive))
        {
            return;
        }

        Refresh(NavigationRefreshScope.All);
    }

    /// <inheritdoc />
    public void ApplyFilterProjection(bool isTreeMode)
    {
        _folderTree.Refresh();
        if (!isTreeMode)
        {
            _noteList.Refresh();
        }
    }

    /// <inheritdoc />
    public void ApplyLayoutRebuild(bool isTreeMode)
    {
        Refresh(NavigationRefreshScope.All);
    }
}
