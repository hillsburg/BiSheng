namespace BiSheng.Latte.Services.Navigation;

/// <summary>本地导航变更发布（FolderTree / NoteList CRUD 统一入口）</summary>
public interface INavigationMutationPublisher
{
    /// <summary>文件夹创建</summary>
    void NotifyFolderCreated(Guid folderId, Guid? parentFolderId);

    /// <summary>文件夹更新</summary>
    void NotifyFolderUpdated(Guid folderId, Guid? parentFolderId, bool flagsChanged = false, bool parentFolderChanged = false);

    /// <summary>文件夹删除</summary>
    void NotifyFolderDeleted(Guid folderId);

    /// <summary>笔记创建</summary>
    void NotifyNoteCreated(Guid noteId, Guid folderId);

    /// <summary>笔记更新</summary>
    void NotifyNoteUpdated(Guid noteId, Guid folderId, bool flagsChanged = false);

    /// <summary>笔记删除</summary>
    void NotifyNoteDeleted(Guid noteId, Guid folderId);

    /// <summary>批量导航变更（回收站清空等）；超阈值时 fallback 全量</summary>
    void NotifyChanges(IReadOnlyList<NavigationChange> changes);

    /// <summary>搜索过滤文本变化</summary>
    void NotifyFilterChanged();

    /// <summary>布局模式或列宽恢复等结构性重建</summary>
    void NotifyLayoutRebuild();
}
