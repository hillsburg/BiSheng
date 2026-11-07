using System.Collections.ObjectModel;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Shared;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 文件夹树 · 增量更新：把导航变更（本地操作或同步拉取）就地应用到现有节点，
/// 避免全量 Refresh 造成的展开状态抖动与选中项丢失。
/// 任何无法安全增量处理的情况都返回 false，由调用方回退到全量刷新。
/// </summary>
public partial class FolderTreeViewModel
{
    /// <summary>增量应用 Folder/树内 Note 变更；失败返回 false</summary>
    public bool ApplyNavigationDelta(IReadOnlyList<NavigationChange> changes, bool isSearchActive)
    {
        // 搜索过滤下节点集合不完整，增量插入无法判断是否该显示，直接全量
        if (isSearchActive)
        {
            return false;
        }

        var favoritesDirty = false;

        foreach (var change in changes)
        {
            // 跨父级移动涉及两处子树的重排，交给全量刷新
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

        // 笔记换了文件夹：旧位置的节点需要移走，交给全量刷新
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

    // ===== 有序插入 =====

    private bool InsertFolderNodeSorted(LocalFolder folder)
    {
        var node = new FolderNode(folder);
        if (folder.ParentId == null)
        {
            InsertFolderChildSorted(RootNodes, node);
            return true;
        }

        // 父节点不在树中时无法定位插入位置，回退全量
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

    /// <summary>子节点集合混有文件夹与笔记：只与文件夹比较，保证文件夹始终排在笔记之前</summary>
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

    // ===== 移除 =====

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

    private void RemoveNoteFromTree(Guid noteId) =>
        RemoveNoteFromChildren(RootNodes, noteId);

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
}
