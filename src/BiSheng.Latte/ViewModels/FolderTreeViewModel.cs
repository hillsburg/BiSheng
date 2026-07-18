using System.Collections.ObjectModel;
using System.Windows;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using BiSheng.Latte.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 文件夹树形结构视图模型，管理文件夹的 CRUD、展开/折叠状态持久化及内联重命名。
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
        if (renamingNode != null) renamingNode.IsRenaming = false;

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
                RootNodes.Add(BuildNode(rootFolder, lookup, notesByFolder, search));
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
            if (node != null) { node.RenameText = renamingText ?? node.Name; node.IsRenaming = true; }
        }

        // 归纳模式：恢复笔记重命名状态
        if (renamingNoteId.HasValue && _noteList != null)
        {
            var noteItem = FindNoteInTree(RootNodes, renamingNoteId.Value);
            if (noteItem != null) { noteItem.RenameText = renamingNoteText ?? noteItem.Title; noteItem.IsRenaming = true; }
        }
    }

    /// <summary>是否有文件夹/树内笔记正在内联重命名</summary>
    public bool IsInlineRenamingActive =>
        FindRenamingFolderNode(RootNodes) != null
        || (IncludeNotes && FindRenamingNoteInTree(RootNodes) != null)
        || (_noteList?.Notes.Any(n => n.IsRenaming) ?? false);

    /// <summary>增量应用 Folder/树内 Note 变更；失败返回 false</summary>
    public bool ApplyNavigationDelta(IReadOnlyList<NavigationChange> changes, bool isSearchActive)
    {
        if (isSearchActive)
        {
            return false;
        }

        var favoritesDirty = false;

        foreach (var change in changes)
        {
            if (change.ParentFolderChanged)
            {
                return false;
            }

            if (change.EntityType == EntityTypes.Folder)
            {
                if (!ApplyFolderNavigationChange(change))
                {
                    return false;
                }

                if (change.FlagsChanged)
                {
                    favoritesDirty = true;
                }
            }
            else if (change.EntityType == EntityTypes.Note && IncludeNotes)
            {
                if (!ApplyTreeNoteNavigationChange(change))
                {
                    return false;
                }

                if (change.FlagsChanged)
                {
                    favoritesDirty = true;
                }
            }
        }

        if (favoritesDirty)
        {
            RebuildFavorites();
        }

        return true;
    }

    private bool ApplyFolderNavigationChange(NavigationChange change)
    {
        if (change.Action == ChangeActions.Delete)
        {
            RemoveFolderNodeFromTree(change.EntityId);
            return true;
        }

        using var db = _dbFactory();
        var folder = db.Folders.Find(change.EntityId);
        if (folder == null || folder.IsDeleted)
        {
            RemoveFolderNodeFromTree(change.EntityId);
            return true;
        }

        var node = FindNodeById(RootNodes, folder.Id);
        if (node == null)
        {
            return InsertFolderNodeSorted(folder);
        }

        LocalFolderMerger.MergeFields(node.Folder, folder);
        node.NotifyDisplayChanged();
        return true;
    }

    private bool ApplyTreeNoteNavigationChange(NavigationChange change)
    {
        using var db = _dbFactory();
        var note = db.Notes.Find(change.EntityId);

        if (change.Action == ChangeActions.Delete || note == null || note.IsDeleted)
        {
            RemoveNoteFromTree(change.EntityId);
            return true;
        }

        var existing = FindNoteInTree(RootNodes, change.EntityId);
        if (existing != null && existing.FolderId != note.FolderId)
        {
            return false;
        }

        if (existing == null)
        {
            return InsertTreeNoteSorted(note);
        }

        LocalNoteMerger.MergeFields(existing.Note, note);
        existing.NotifyDisplayChanged();
        return true;
    }

    private bool InsertFolderNodeSorted(LocalFolder folder)
    {
        var node = new FolderNode(folder);
        if (folder.ParentId == null)
        {
            InsertFolderChildSorted(RootNodes, node);
            return true;
        }

        var parent = FindNodeById(RootNodes, folder.ParentId.Value);
        if (parent == null)
        {
            return false;
        }

        InsertFolderChildSorted(parent.Children, node);
        return true;
    }

    private static void InsertFolderChildSorted(IList<FolderNode> collection, FolderNode node)
    {
        var comparer = Comparer<LocalFolder>.Create(NavSortHelper.CompareFolder);
        var index = 0;
        for (; index < collection.Count; index++)
        {
            if (comparer.Compare(node.Folder, collection[index].Folder) < 0)
            {
                break;
            }
        }

        collection.Insert(index, node);
    }

    private static void InsertFolderChildSorted(ObservableCollection<object> collection, FolderNode node)
    {
        var comparer = Comparer<LocalFolder>.Create(NavSortHelper.CompareFolder);
        var index = 0;
        for (; index < collection.Count; index++)
        {
            if (collection[index] is FolderNode fn
                && comparer.Compare(node.Folder, fn.Folder) < 0)
            {
                break;
            }
        }

        collection.Insert(index, node);
    }

    private bool InsertTreeNoteSorted(LocalNote note)
    {
        var parent = FindNodeById(RootNodes, note.FolderId);
        if (parent == null)
        {
            return false;
        }

        var item = new NoteItemViewModel(note);
        var comparer = Comparer<LocalNote>.Create(NavSortHelper.CompareNote);
        var index = 0;
        for (; index < parent.Children.Count; index++)
        {
            if (parent.Children[index] is NoteItemViewModel ni
                && comparer.Compare(note, ni.Note) < 0)
            {
                break;
            }
        }

        parent.Children.Insert(index, item);
        return true;
    }

    private void RemoveFolderNodeFromTree(Guid folderId) =>
        RemoveFromRootNodes(RootNodes, folderId);

    private static bool RemoveFromRootNodes(IList<FolderNode> nodes, Guid folderId)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Id == folderId)
            {
                nodes.RemoveAt(i);
                return true;
            }

            if (RemoveFromChildren(nodes[i].Children, folderId))
            {
                return true;
            }
        }

        return false;
    }

    private void RemoveNoteFromTree(Guid noteId) =>
        RemoveNoteFromChildren(RootNodes, noteId);

    private static bool RemoveFromChildren(IList<object> nodes, Guid folderId)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is FolderNode fn)
            {
                if (fn.Id == folderId)
                {
                    nodes.RemoveAt(i);
                    return true;
                }

                if (RemoveFromChildren(fn.Children, folderId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void RemoveNoteFromChildren(IEnumerable<object> nodes, Guid noteId)
    {
        foreach (var obj in nodes)
        {
            if (obj is not FolderNode fn)
            {
                continue;
            }

            for (var i = fn.Children.Count - 1; i >= 0; i--)
            {
                if (fn.Children[i] is NoteItemViewModel ni && ni.Id == noteId)
                {
                    fn.Children.RemoveAt(i);
                }
            }

            RemoveNoteFromChildren(fn.Children, noteId);
        }
    }

    /// <summary>
    /// 选中文件夹变化时，触发文件夹选中事件。
    /// </summary>
    partial void OnSelectedFolderChanged(LocalFolder? value)
    {
        if (value != null) OnFolderSelected?.Invoke(value.Id);
    }

    /// <summary>
    /// 创建根级文件夹（无父级）。
    /// </summary>
    [RelayCommand]
    private void CreateFolder() { CreateFolderInternal(parentId: null); }

    /// <summary>
    /// 在当前选中文件夹下创建子文件夹。
    /// </summary>
    [RelayCommand]
    private void CreateSubFolder()
    {
        if (SelectedFolder == null) return;
        CreateFolderInternal(parentId: SelectedFolder.Id);
    }

    /// <summary>
    /// 在当前选中文件夹的同级创建新文件夹。
    /// </summary>
    [RelayCommand]
    private void CreateSiblingFolder()
    {
        if (SelectedFolder == null) return;
        CreateFolderInternal(parentId: SelectedFolder.ParentId);
    }

    /// <summary>
    /// 创建文件夹的核心逻辑，持久化后自动进入重命名模式。
    /// </summary>
    /// <param name="parentId">父文件夹 ID，null 表示根级。</param>
    private void CreateFolderInternal(Guid? parentId)
    {
        var defaultName = GenerateUniqueFolderName(parentId);
        var folder = new LocalFolder { Name = defaultName, ParentId = parentId };
        using var db = _dbFactory();
        db.Folders.Add(folder);
        db.SaveChangesWithLock();
        _changeTracker.RecordChange(EntityTypes.Folder, folder.Id, ChangeActions.Create,
            SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));
        _navigationPublisher.NotifyFolderCreated(folder.Id, parentId);
        var node = FindNodeById(RootNodes, folder.Id);
        if (node != null)
        {
            // 展开父节点，确保新建子文件夹可见并显示重命名输入框
            if (parentId.HasValue)
            {
                var parentNode = FindNodeById(RootNodes, parentId.Value);
                if (parentNode != null) parentNode.IsExpanded = true;
            }

            SelectedFolder = folder;
            node.RenameText = node.Name;
            node.IsRenaming = true; // 创建后立即进入内联重命名
        }
    }

    /// <summary>
    /// 开始重命名当前选中的文件夹。
    /// </summary>
    public void StartRenaming()
    {
        if (SelectedFolder == null) return;
        var node = FindNodeById(RootNodes, SelectedFolder.Id);
        if (node == null) return;
        node.RenameText = node.Name;
        node.IsRenaming = true;
    }

    /// <summary>
    /// 确认重命名：校验名称唯一性后持久化到数据库。
    /// </summary>
    /// <param name="node">正在重命名的文件夹节点。</param>
    public void CommitRename(FolderNode node)
    {
        // 防止失焦与窗口点击重复提交
        if (!node.IsRenaming) return;

        var newName = node.RenameText?.Trim();
        // 空名或未修改：保留默认/原名并退出重命名
        if (string.IsNullOrEmpty(newName)) { node.RenameText = node.Name; node.IsRenaming = false; return; }
        if (newName == node.Name) { node.IsRenaming = false; return; }
        if (IsFolderNameDuplicate(newName, node.ParentId, node.Id)) { node.RenameText = node.Name; node.IsRenaming = false; return; }
        using var db = _dbFactory();
        var folder = db.Folders.Find(node.Id);
        if (folder != null) { folder.Name = newName; folder.UpdatedAt = DateTime.UtcNow; db.SaveChangesWithLock(); }
        _changeTracker.RecordChange(EntityTypes.Folder, node.Id, ChangeActions.Update,
            SyncPayloadBuilder.Folder(newName, node.ParentId, folder?.IsFavorite ?? false, folder?.IsPinned ?? false));
        node.IsRenaming = false;
        _navigationPublisher.NotifyFolderUpdated(node.Id, node.ParentId);
    }

    /// <summary>
    /// 取消重命名，恢复原始名称。
    /// </summary>
    /// <param name="node">正在重命名的文件夹节点。</param>
    public void CancelRename(FolderNode node) { node.RenameText = node.Name; node.IsRenaming = false; }

    /// <summary>
    /// 删除选中的文件夹（软删除），并记录变更供同步引擎使用。
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedFolder()
    {
        if (SelectedFolder == null) return;
        using var db = _dbFactory();
        var folder = db.Folders.Find(SelectedFolder.Id);
        if (folder != null)
        {
            folder.IsDeleted = true;
            folder.DeletedAt = DateTime.UtcNow;
            folder.UpdatedAt = DateTime.UtcNow;
            db.SaveChangesWithLock();
            _changeTracker.RecordChange(EntityTypes.Folder, folder.Id, ChangeActions.Delete);
        }

        _navigationPublisher.NotifyFolderDeleted(SelectedFolder.Id);
    }

    /// <summary>切换文件夹收藏状态</summary>
    public void ToggleFavoriteFolder(FolderNode node)
    {
        using var db = _dbFactory();
        var folder = db.Folders.Find(node.Id);
        if (folder == null) return;
        folder.IsFavorite = !folder.IsFavorite;
        folder.UpdatedAt = DateTime.UtcNow;
        db.SaveChangesWithLock();
        _changeTracker.RecordChange(EntityTypes.Folder, folder.Id, ChangeActions.Update,
            SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));
        _navigationPublisher.NotifyFolderUpdated(folder.Id, folder.ParentId, flagsChanged: true);
    }

    /// <summary>切换文件夹置顶状态</summary>
    public void TogglePinFolder(FolderNode node)
    {
        using var db = _dbFactory();
        var folder = db.Folders.Find(node.Id);
        if (folder == null) return;
        folder.IsPinned = !folder.IsPinned;
        folder.UpdatedAt = DateTime.UtcNow;
        db.SaveChangesWithLock();
        _changeTracker.RecordChange(EntityTypes.Folder, folder.Id, ChangeActions.Update,
            SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));
        _navigationPublisher.NotifyFolderUpdated(folder.Id, folder.ParentId, flagsChanged: true);
    }

    /// <summary>切换笔记收藏状态（归纳模式树内笔记）</summary>
    public void ToggleFavoriteNote(NoteItemViewModel item) => _noteList?.ToggleFavorite(item);

    /// <summary>切换笔记置顶状态（归纳模式树内笔记）</summary>
    public void TogglePinNote(NoteItemViewModel item) => _noteList?.TogglePin(item);

    /// <summary>重建收藏快捷列表</summary>
    public void RebuildFavorites()
    {
        if (_navigation == null) return;

        _navigation.FavoriteItems.Clear();
        using var db = _dbFactory();

        var items = new List<object>();
        foreach (var folder in db.Folders.Where(f => !f.IsDeleted && f.IsFavorite))
            items.Add(new FolderNode(folder));
        foreach (var note in db.Notes.Where(n => !n.IsDeleted && n.IsFavorite))
            items.Add(new NoteItemViewModel(note));

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
        if (string.IsNullOrWhiteSpace(search)) return true;
        var text = item switch
        {
            FolderNode fn => fn.Name,
            NoteItemViewModel ni => ni.Title,
            _ => string.Empty
        };
        return text.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool MatchesSearch(string text, string? search)
    {
        return string.IsNullOrWhiteSpace(search)
               || text.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private static bool ShouldIncludeFolder(
        LocalFolder folder,
        ILookup<Guid?, LocalFolder> lookup,
        ILookup<Guid, LocalNote>? notesByFolder,
        string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        if (MatchesSearch(folder.Name, search)) return true;

        foreach (var child in lookup[folder.Id])
        {
            if (ShouldIncludeFolder(child, lookup, notesByFolder, search))
                return true;
        }

        if (notesByFolder != null)
        {
            foreach (var note in notesByFolder[folder.Id])
            {
                if (MatchesSearch(note.Title, search))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 递归构建文件夹节点树。归纳模式下同时加载笔记作为叶节点。
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
                    mixed.Add((0, BuildNode(child, lookup, notesByFolder, search)));
            }

            foreach (var n in notesByFolder[folder.Id]
                         .Where(note => MatchesSearch(note.Title, search))
                         .OrderBy(n => n, Comparer<LocalNote>.Create(NavSortHelper.CompareNote)))
            {
                mixed.Add((1, new NoteItemViewModel(n)));
            }

            foreach (var entry in mixed.OrderBy(x => x.SortKey))
                node.Children.Add(entry.Item);
        }
        else
        {
            foreach (var child in lookup[folder.Id].OrderBy(f => f, Comparer<LocalFolder>.Create(NavSortHelper.CompareFolder)))
            {
                if (ShouldIncludeFolder(child, lookup, notesByFolder, search))
                    node.Children.Add(BuildNode(child, lookup, notesByFolder, search));
            }
        }

        return node;
    }

    /// <summary>
    /// 递归收集所有展开节点的 ID，用于刷新后恢复展开状态。
    /// </summary>
    private HashSet<Guid> CollectExpandedIds(IEnumerable<object> nodes)
    {
        var ids = new HashSet<Guid>();
        foreach (var obj in nodes)
        {
            if (obj is FolderNode node)
            {
                if (node.IsExpanded) ids.Add(node.Id);
                ids.UnionWith(CollectExpandedIds(node.Children));
            }
        }

        return ids;
    }

    /// <summary>
    /// 获取所有展开文件夹的 ID 列表（字符串形式），用于持久化。
    /// </summary>
    public List<string> GetExpandedFolderIds() => CollectExpandedIds(RootNodes).Select(id => id.ToString()).ToList();

    /// <summary>
    /// 从持久化的 ID 列表恢复文件夹展开状态。
    /// </summary>
    /// <param name="ids">需要展开的文件夹 ID 集合。</param>
    public void RestoreExpandedFolderIds(IEnumerable<string> ids)
    {
        var guidSet = new HashSet<Guid>(ids.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty));
        RestoreExpanded(RootNodes, guidSet);
    }

    /// <summary>
    /// 递归恢复指定 ID 集合中文件夹节点的展开状态。
    /// </summary>
    private void RestoreExpanded(IEnumerable<object> nodes, HashSet<Guid> expandedIds)
    {
        foreach (var obj in nodes)
        {
            if (obj is FolderNode node)
            {
                if (expandedIds.Contains(node.Id)) node.IsExpanded = true;
                RestoreExpanded(node.Children, expandedIds);
            }
        }
    }

    /// <summary>
    /// 在节点树中按 ID 深度优先查找文件夹节点。
    /// </summary>
    public FolderNode? FindNodeById(IEnumerable<object> nodes, Guid id)
    {
        foreach (var obj in nodes)
        {
            if (obj is FolderNode node)
            {
                if (node.Id == id) return node;
                var found = FindNodeById(node.Children, id);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>展开至目标文件夹的各级父节点，便于在树中定位笔记</summary>
    public void ExpandPathToFolder(Guid folderId)
    {
        using var db = _dbFactory();
        var chain = new List<Guid>();
        var id = folderId;

        while (true)
        {
            chain.Add(id);
            var folder = db.Folders.Find(id);
            if (folder?.ParentId == null)
            {
                break;
            }

            id = folder.ParentId.Value;
        }

        chain.Reverse();
        foreach (var fid in chain)
        {
            var node = FindNodeById(RootNodes, fid);
            if (node != null)
            {
                node.IsExpanded = true;
            }
        }
    }

    /// <summary>
    /// 递归查找正处于重命名状态的文件夹节点。
    /// </summary>
    private FolderNode? FindRenamingFolderNode(IEnumerable<object> nodes)
    {
        foreach (var obj in nodes)
        {
            if (obj is FolderNode node)
            {
                if (node.IsRenaming) return node;
                var found = FindRenamingFolderNode(node.Children);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// 递归查找正处于重命名状态的笔记叶节点（归纳模式）。
    /// </summary>
    private NoteItemViewModel? FindRenamingNoteInTree(IEnumerable<object> nodes)
    {
        foreach (var obj in nodes)
        {
            if (obj is NoteItemViewModel ni && ni.IsRenaming) return ni;
            if (obj is FolderNode fn)
            {
                var found = FindRenamingNoteInTree(fn.Children);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>
    /// 在树中按 ID 查找笔记叶节点（归纳模式）。
    /// </summary>
    public NoteItemViewModel? FindNoteInTree(IEnumerable<object> nodes, Guid noteId)
    {
        foreach (var obj in nodes)
        {
            if (obj is NoteItemViewModel ni && ni.Id == noteId) return ni;
            if (obj is FolderNode fn)
            {
                var found = FindNoteInTree(fn.Children, noteId);
                if (found != null) return found;
            }
        }
        return null;
    }

    /// <summary>在归纳模式下开始重命名笔记</summary>
    public void StartRenamingNote(NoteItemViewModel item)
    {
        item.RenameText = item.Title;
        item.IsRenaming = true;
    }

    /// <summary>在归纳模式下提交笔记重命名，委托给 NoteListViewModel</summary>
    public void CommitNoteRename(NoteItemViewModel item)
    {
        _noteList?.CommitRename(item);
    }

    /// <summary>在归纳模式下取消笔记重命名</summary>
    public void CancelNoteRename(NoteItemViewModel item)
    {
        _noteList?.CancelRename(item);
    }

    /// <summary>
    /// 按 ID 查找文件夹实体（不返回节点包装）。
    /// </summary>
    /// <param name="id">目标文件夹 ID。</param>
    /// <returns>匹配的文件夹实体，未找到返回 null。</returns>
    public LocalFolder? FindFolderById(Guid id) => FindNodeById(RootNodes, id)?.Folder;

    /// <summary>
    /// 获取所有文件夹实体的扁平列表（含子文件夹递归展开）。
    /// </summary>
    public IEnumerable<LocalFolder> GetAllFolders() => GetAllFoldersInternal(RootNodes);
    /// <summary>
    /// 递归遍历节点树，逐个返回文件夹实体。
    /// </summary>
    private IEnumerable<LocalFolder> GetAllFoldersInternal(IEnumerable<object> nodes)
    {
        foreach (var obj in nodes)
        {
            if (obj is FolderNode node)
            {
                yield return node.Folder;
                foreach (var child in GetAllFoldersInternal(node.Children))
                    yield return child;
            }
        }
    }

    /// <summary>
    /// 将选中文件夹导出为 Markdown 文件，弹出目录选择对话框。
    /// </summary>
    [RelayCommand]
    private async Task ExportFolderAsMarkdownAsync()
    {
        if (SelectedFolder == null || _exportService == null) return;
        var dialog = new OpenFolderDialog { Title = "选择 Markdown 导出目录" };
        if (dialog.ShowDialog() == true) { try { await _exportService.ExportFolderAsMarkdownAsync(SelectedFolder, dialog.FolderName); AppDialog.Success("文件夹已导出到:\n" + dialog.FolderName, "导出成功"); } catch (Exception ex) { AppDialog.Error("导出失败: " + ex.Message, "导出失败"); } }
    }

    /// <summary>
    /// 将选中文件夹导出为 Word 文件，弹出目录选择对话框。
    /// </summary>
    [RelayCommand]
    private async Task ExportFolderAsWordAsync()
    {
        if (SelectedFolder == null || _exportService == null) return;
        var dialog = new OpenFolderDialog { Title = "选择 Word 导出目录" };
        if (dialog.ShowDialog() == true) { try { await _exportService.ExportFolderAsWordAsync(SelectedFolder, dialog.FolderName); AppDialog.Success("文件夹已导出到:\n" + dialog.FolderName, "导出成功"); } catch (Exception ex) { AppDialog.Error("导出失败: " + ex.Message, "导出失败"); } }
    }

    /// <summary>
    /// 将选中文件夹导出为 PDF 文件，弹出目录选择对话框。
    /// </summary>
    [RelayCommand]
    private async Task ExportFolderAsPdfAsync()
    {
        if (SelectedFolder == null || _exportService == null) return;
        var dialog = new OpenFolderDialog { Title = "选择 PDF 导出目录" };
        if (dialog.ShowDialog() == true) { try { await _exportService.ExportFolderAsPdfAsync(SelectedFolder, dialog.FolderName); AppDialog.Success("文件夹已导出到:\n" + dialog.FolderName, "导出成功"); } catch (Exception ex) { AppDialog.Error("导出失败: " + ex.Message, "导出失败"); } }
    }

    /// <summary>
    /// 生成不重复的文件夹名称，同名时自动追加 _1、_2 等后缀。
    /// </summary>
    /// <param name="parentId">父文件夹 ID，null 表示根级。</param>
    /// <param name="baseName">基础名称，默认"新建文件夹"。</param>
    /// <returns>唯一可用的文件夹名称。</returns>
    public string GenerateUniqueFolderName(Guid? parentId, string baseName = "新建文件夹")
    {
        using var db = _dbFactory();
        var existingNames = db.Folders.Where(f => f.ParentId == parentId && !f.IsDeleted).Select(f => f.Name).ToHashSet();
        if (!existingNames.Contains(baseName)) return baseName;
        for (int i = 1; ; i++) { var candidate = baseName + "_" + i; if (!existingNames.Contains(candidate)) return candidate; }
    }

    /// <summary>
    /// 检查同父级下是否存在同名文件夹（排除指定 ID）。
    /// </summary>
    /// <param name="name">待检查的名称。</param>
    /// <param name="parentId">父文件夹 ID。</param>
    /// <param name="excludeId">需排除的文件夹 ID（自身）。</param>
    /// <returns>存在同名返回 true，否则 false。</returns>
    private bool IsFolderNameDuplicate(string name, Guid? parentId, Guid excludeId)
    {
        using var db = _dbFactory();
        return db.Folders.Any(f => f.ParentId == parentId && f.Name == name && f.Id != excludeId && !f.IsDeleted);
    }
}
