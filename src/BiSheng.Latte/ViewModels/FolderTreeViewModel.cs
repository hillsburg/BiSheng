using System.Collections.ObjectModel;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 文件夹树形结构视图模型，管理文件夹的 CRUD、展开/折叠状态持久化及内联重命名。
/// 按职责拆分为多个 partial 文件：
/// 本文件负责全量加载、搜索过滤与收藏列表；.Delta 负责导航变更的增量应用；
/// .Commands 负责新建 / 重命名 / 删除 / 收藏置顶；.Export 负责导出；.TreeQueries 负责树遍历与展开状态。
/// </summary>
public partial class FolderTreeViewModel : ObservableObject
{
    private readonly LocalChangeTracker _changeTracker;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly INavigationMutationPublisher _navigationPublisher;
    private readonly ExportService? _exportService;
    private NoteListViewModel? _noteList;
    private NavigationViewModel? _navigation;
    private readonly INavigationFilterState _filterState;

    public ObservableCollection<FolderNode> RootNodes { get; } = new();

    [ObservableProperty]
    private LocalFolder? _selectedFolder;

    /// <summary>是否在文件夹树中包含笔记叶节点（归纳模式）</summary>
    public bool IncludeNotes { get; set; }

    public event Action<Guid>? OnFolderSelected;

    public FolderTreeViewModel(
        LocalChangeTracker changeTracker,
        Func<LocalDbContext> dbFactory,
        INavigationMutationPublisher navigationPublisher,
        INavigationFilterState filterState,
        ExportService? exportService = null)
    {
        _changeTracker = changeTracker;
        _dbFactory = dbFactory;
        _navigationPublisher = navigationPublisher;
        _filterState = filterState;
        _exportService = exportService;
    }

    /// <summary>设置笔记列表引用，供归纳模式下操作笔记使用</summary>
    public void SetNoteList(NoteListViewModel noteList) => _noteList = noteList;

    /// <summary>设置导航共享状态（搜索、收藏区）</summary>
    public void SetNavigation(NavigationViewModel navigation) => _navigation = navigation;

    /// <summary>是否有文件夹/树内笔记正在内联重命名</summary>
    public bool IsInlineRenamingActive =>
        FindRenamingFolderNode(RootNodes) != null
        || (IncludeNotes && FindRenamingNoteInTree(RootNodes) != null)
        || (_noteList?.Notes.Any(n => n.IsRenaming) ?? false);

    /// <summary>
    /// 选中文件夹变化时，触发文件夹选中事件。
    /// </summary>
    partial void OnSelectedFolderChanged(LocalFolder? value)
    {
        if (value != null)
        {
            OnFolderSelected?.Invoke(value.Id);
        }
    }

    /// <summary>
    /// 刷新文件夹树：保留展开状态、选中项和重命名状态，从本地数据库重新加载。
    /// </summary>
    public void Refresh()
    {
        var expandedIds = CollectExpandedIds(RootNodes);
        var selectedId = SelectedFolder?.Id;
        var renamingNode = FindRenamingFolderNode(RootNodes);
        var renamingId = renamingNode?.Id;
        var renamingText = renamingNode?.RenameText;
        if (renamingNode != null)
        {
            renamingNode.IsRenaming = false;
        }

        // 归纳模式：保存笔记重命名状态
        Guid? renamingNoteId = null;
        string? renamingNoteText = null;
        if (IncludeNotes && _noteList != null)
        {
            var renamingNote = FindRenamingNoteInTree(RootNodes);
            if (renamingNote != null)
            {
                renamingNoteId = renamingNote.Id;
                renamingNoteText = renamingNote.RenameText;
                renamingNote.IsRenaming = false;
            }
        }

        using var db = _dbFactory();
        var folders = db.Folders.Where(f => !f.IsDeleted).ToList();
        var lookup = folders.ToLookup(f => f.ParentId);
        var notesByFolder = IncludeNotes
            ? db.Notes.Where(n => !n.IsDeleted).ToLookup(n => n.FolderId)
            : null;
        var search = _filterState.SearchText;

        RootNodes.Clear();
        foreach (var rootFolder in lookup[null].OrderBy(f => f, Comparer<LocalFolder>.Create(NavSortHelper.CompareFolder)))
        {
            if (ShouldIncludeFolder(rootFolder, lookup, notesByFolder, search))
            {
                RootNodes.Add(BuildNode(rootFolder, lookup, notesByFolder, search));
            }
        }

        RebuildFavorites();

        RestoreExpanded(RootNodes, expandedIds);
        if (selectedId.HasValue)
        {
            var node = FindNodeById(RootNodes, selectedId.Value);
            // Id 不变时不替换 SelectedFolder 引用：
            // 替换成新对象会触发 OnSelectedFolderChanged → OnFolderSelected → NoteList.LoadNotes，
            // LoadNotes 清空 Notes 集合会让 SelectedNote 变 null → 编辑器被清空。
            // 业务只用 SelectedFolder.Id，字段更新靠 Tree 节点显示，无需替换引用
            if (node != null && SelectedFolder?.Id != node.Folder.Id)
            {
                SelectedFolder = node.Folder;
            }
        }

        if (renamingId.HasValue)
        {
            var node = FindNodeById(RootNodes, renamingId.Value);
            if (node != null)
            {
                node.RenameText = renamingText ?? node.Name;
                node.IsRenaming = true;
            }
        }

        // 归纳模式：恢复笔记重命名状态
        if (renamingNoteId.HasValue && _noteList != null)
        {
            var noteItem = FindNoteInTree(RootNodes, renamingNoteId.Value);
            if (noteItem != null)
            {
                noteItem.RenameText = renamingNoteText ?? noteItem.Title;
                noteItem.IsRenaming = true;
            }
        }
    }

    /// <summary>
    /// 递归构建文件夹节点树。归纳模式下同时加载笔记作为叶节点（子文件夹在前、笔记在后）。
    /// </summary>
    private FolderNode BuildNode(
        LocalFolder folder,
        ILookup<Guid?, LocalFolder> lookup,
        ILookup<Guid, LocalNote>? notesByFolder,
        string? search)
    {
        var node = new FolderNode(folder);

        if (IncludeNotes && notesByFolder != null)
        {
            var mixed = new List<(int SortKey, object Item)>();
            foreach (var child in lookup[folder.Id].OrderBy(f => f, Comparer<LocalFolder>.Create(NavSortHelper.CompareFolder)))
            {
                if (ShouldIncludeFolder(child, lookup, notesByFolder, search))
                {
                    mixed.Add((0, BuildNode(child, lookup, notesByFolder, search)));
                }
            }

            foreach (var n in notesByFolder[folder.Id]
                         .Where(note => MatchesSearch(note.Title, search))
                         .OrderBy(n => n, Comparer<LocalNote>.Create(NavSortHelper.CompareNote)))
            {
                mixed.Add((1, new NoteItemViewModel(n)));
            }

            foreach (var entry in mixed.OrderBy(x => x.SortKey))
            {
                node.Children.Add(entry.Item);
            }
        }
        else
        {
            foreach (var child in lookup[folder.Id].OrderBy(f => f, Comparer<LocalFolder>.Create(NavSortHelper.CompareFolder)))
            {
                if (ShouldIncludeFolder(child, lookup, notesByFolder, search))
                {
                    node.Children.Add(BuildNode(child, lookup, notesByFolder, search));
                }
            }
        }

        return node;
    }

    // ===== 搜索过滤 =====

    private static bool MatchesSearch(string text, string? search)
    {
        return string.IsNullOrWhiteSpace(search)
               || text.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// 搜索时保留：自身名称命中、任一子孙文件夹命中、或（归纳模式）任一直属笔记标题命中的文件夹
    /// </summary>
    private static bool ShouldIncludeFolder(
        LocalFolder folder,
        ILookup<Guid?, LocalFolder> lookup,
        ILookup<Guid, LocalNote>? notesByFolder,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search) || MatchesSearch(folder.Name, search))
        {
            return true;
        }

        foreach (var child in lookup[folder.Id])
        {
            if (ShouldIncludeFolder(child, lookup, notesByFolder, search))
            {
                return true;
            }
        }

        if (notesByFolder != null)
        {
            foreach (var note in notesByFolder[folder.Id])
            {
                if (MatchesSearch(note.Title, search))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ===== 收藏快捷列表 =====

    /// <summary>重建收藏快捷列表</summary>
    public void RebuildFavorites()
    {
        if (_navigation == null)
        {
            return;
        }

        _navigation.FavoriteItems.Clear();
        using var db = _dbFactory();

        var items = new List<object>();
        foreach (var folder in db.Folders.Where(f => !f.IsDeleted && f.IsFavorite))
        {
            items.Add(new FolderNode(folder));
        }

        foreach (var note in db.Notes.Where(n => !n.IsDeleted && n.IsFavorite))
        {
            items.Add(new NoteItemViewModel(note));
        }

        var search = _filterState.SearchText?.Trim();
        foreach (var item in items
                     .Where(i => MatchesFavoriteSearch(i, search))
                     .OrderBy(i => i, Comparer<object>.Create(NavSortHelper.CompareFavoriteItem)))
        {
            _navigation.FavoriteItems.Add(item);
        }

        _navigation.HasFavorites = _navigation.FavoriteItems.Count > 0;
    }

    private static bool MatchesFavoriteSearch(object item, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        var text = item switch
        {
            FolderNode fn => fn.Name,
            NoteItemViewModel ni => ni.Title,
            _ => string.Empty
        };
        return text.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }
}
