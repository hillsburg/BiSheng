using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Shared;
using BiSheng.Shared.Sync;
using CommunityToolkit.Mvvm.Input;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 文件夹树 · 写操作：新建、内联重命名、软删除、收藏 / 置顶。
/// 每次写库后都要记录同步变更（LocalChangeTracker）并发布导航通知，节点的增删由通知驱动，这里不直接改节点集合。
/// </summary>
public partial class FolderTreeViewModel
{
    // ===== 新建 =====

    /// <summary>
    /// 创建根级文件夹（无父级）。
    /// </summary>
    [RelayCommand]
    private void CreateFolder()
    {
        CreateFolderInternal(parentId: null);
    }

    /// <summary>
    /// 在当前选中文件夹下创建子文件夹。
    /// </summary>
    [RelayCommand]
    private void CreateSubFolder()
    {
        if (SelectedFolder == null)
        {
            return;
        }

        CreateFolderInternal(parentId: SelectedFolder.Id);
    }

    /// <summary>
    /// 在当前选中文件夹的同级创建新文件夹。
    /// </summary>
    [RelayCommand]
    private void CreateSiblingFolder()
    {
        if (SelectedFolder == null)
        {
            return;
        }

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
                if (parentNode != null)
                {
                    parentNode.IsExpanded = true;
                }
            }

            SelectedFolder = folder;
            node.RenameText = node.Name;
            node.IsRenaming = true; // 创建后立即进入内联重命名
        }
    }

    // ===== 文件夹重命名 =====

    /// <summary>
    /// 开始重命名当前选中的文件夹。
    /// </summary>
    public void StartRenaming()
    {
        if (SelectedFolder == null)
        {
            return;
        }

        var node = FindNodeById(RootNodes, SelectedFolder.Id);
        if (node == null)
        {
            return;
        }

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
        if (!node.IsRenaming)
        {
            return;
        }

        var newName = node.RenameText?.Trim();

        // 空名或未修改：保留默认/原名并退出重命名
        if (string.IsNullOrEmpty(newName))
        {
            CancelRename(node);
            return;
        }

        if (newName == node.Name)
        {
            node.IsRenaming = false;
            return;
        }

        // 同级重名：静默恢复原名
        if (IsFolderNameDuplicate(newName, node.ParentId, node.Id))
        {
            CancelRename(node);
            return;
        }

        using var db = _dbFactory();
        var folder = db.Folders.Find(node.Id);
        if (folder != null)
        {
            folder.Name = newName;
            folder.UpdatedAt = DateTime.UtcNow;
            db.SaveChangesWithLock();
        }

        _changeTracker.RecordChange(EntityTypes.Folder, node.Id, ChangeActions.Update,
            SyncPayloadBuilder.Folder(newName, node.ParentId, folder?.IsFavorite ?? false, folder?.IsPinned ?? false));
        node.IsRenaming = false;
        _navigationPublisher.NotifyFolderUpdated(node.Id, node.ParentId);
    }

    /// <summary>
    /// 取消重命名，恢复原始名称。
    /// </summary>
    /// <param name="node">正在重命名的文件夹节点。</param>
    public void CancelRename(FolderNode node)
    {
        node.RenameText = node.Name;
        node.IsRenaming = false;
    }

    // ===== 树内笔记重命名（归纳模式，委托给 NoteListViewModel） =====

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

    // ===== 删除 =====

    /// <summary>
    /// 删除选中的文件夹（软删除），并记录变更供同步引擎使用。
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedFolder()
    {
        if (SelectedFolder == null)
        {
            return;
        }

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

    // ===== 收藏 / 置顶 =====

    /// <summary>切换文件夹收藏状态</summary>
    public void ToggleFavoriteFolder(FolderNode node)
    {
        UpdateFolderFlags(node.Id, folder => folder.IsFavorite = !folder.IsFavorite);
    }

    /// <summary>切换文件夹置顶状态</summary>
    public void TogglePinFolder(FolderNode node)
    {
        UpdateFolderFlags(node.Id, folder => folder.IsPinned = !folder.IsPinned);
    }

    /// <summary>切换笔记收藏状态（归纳模式树内笔记）</summary>
    public void ToggleFavoriteNote(NoteItemViewModel item) => _noteList?.ToggleFavorite(item);

    /// <summary>切换笔记置顶状态（归纳模式树内笔记）</summary>
    public void TogglePinNote(NoteItemViewModel item) => _noteList?.TogglePin(item);

    /// <summary>
    /// 修改文件夹的收藏 / 置顶标记并持久化。flagsChanged 通知会让收藏区与排序一并刷新。
    /// </summary>
    private void UpdateFolderFlags(Guid folderId, Action<LocalFolder> mutate)
    {
        using var db = _dbFactory();
        var folder = db.Folders.Find(folderId);
        if (folder == null)
        {
            return;
        }

        mutate(folder);
        folder.UpdatedAt = DateTime.UtcNow;
        db.SaveChangesWithLock();
        _changeTracker.RecordChange(EntityTypes.Folder, folder.Id, ChangeActions.Update,
            SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));
        _navigationPublisher.NotifyFolderUpdated(folder.Id, folder.ParentId, flagsChanged: true);
    }

    // ===== 命名 =====

    /// <summary>
    /// 生成不重复的文件夹名称，同名时自动追加 _1、_2 等后缀。
    /// </summary>
    /// <param name="parentId">父文件夹 ID，null 表示根级。</param>
    /// <param name="baseName">基础名称，默认"新建文件夹"。</param>
    /// <returns>唯一可用的文件夹名称。</returns>
    public string GenerateUniqueFolderName(Guid? parentId, string baseName = "新建文件夹")
    {
        using var db = _dbFactory();
        var existingNames = db.Folders
            .Where(f => f.ParentId == parentId && !f.IsDeleted)
            .Select(f => f.Name)
            .ToHashSet();
        if (!existingNames.Contains(baseName))
        {
            return baseName;
        }

        for (int i = 1; ; i++)
        {
            var candidate = baseName + "_" + i;
            if (!existingNames.Contains(candidate))
            {
                return candidate;
            }
        }
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
