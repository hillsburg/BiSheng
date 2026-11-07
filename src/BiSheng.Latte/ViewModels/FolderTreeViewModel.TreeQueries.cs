using BiSheng.Latte.Data.Entities;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 文件夹树 · 树遍历：按 ID 查找节点、展开状态的收集与恢复、重命名中节点的定位。
/// 子节点集合是 object 混合列表（FolderNode 与归纳模式下的 NoteItemViewModel），遍历时按类型分流。
/// </summary>
public partial class FolderTreeViewModel
{
    // ===== 展开状态 =====

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
                if (node.IsExpanded)
                {
                    ids.Add(node.Id);
                }

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
    /// 从持久化的 ID 列表恢复文件夹展开状态。无法解析的 ID 被忽略。
    /// </summary>
    /// <param name="ids">需要展开的文件夹 ID 集合。</param>
    public void RestoreExpandedFolderIds(IEnumerable<string> ids)
    {
        var guidSet = new HashSet<Guid>(ids
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty));
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
                if (expandedIds.Contains(node.Id))
                {
                    node.IsExpanded = true;
                }

                RestoreExpanded(node.Children, expandedIds);
            }
        }
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

    // ===== 查找 =====

    /// <summary>
    /// 在节点树中按 ID 深度优先查找文件夹节点。
    /// </summary>
    public FolderNode? FindNodeById(IEnumerable<object> nodes, Guid id)
    {
        foreach (var obj in nodes)
        {
            if (obj is FolderNode node)
            {
                if (node.Id == id)
                {
                    return node;
                }

                var found = FindNodeById(node.Children, id);
                if (found != null)
                {
                    return found;
                }
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
            if (obj is NoteItemViewModel ni && ni.Id == noteId)
            {
                return ni;
            }

            if (obj is FolderNode fn)
            {
                var found = FindNoteInTree(fn.Children, noteId);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
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
                if (node.IsRenaming)
                {
                    return node;
                }

                var found = FindRenamingFolderNode(node.Children);
                if (found != null)
                {
                    return found;
                }
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
            if (obj is NoteItemViewModel ni && ni.IsRenaming)
            {
                return ni;
            }

            if (obj is FolderNode fn)
            {
                var found = FindRenamingNoteInTree(fn.Children);
                if (found != null)
                {
                    return found;
                }
            }
        }

        return null;
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
                {
                    yield return child;
                }
            }
        }
    }
}
