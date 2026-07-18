using BiSheng.Latte.Data.Entities;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>导航刷新范围</summary>
public enum NavigationRefreshScope
{
    /// <summary>文件夹树 + 笔记列表</summary>
    All,

    /// <summary>仅文件夹树</summary>
    FolderTree,

    /// <summary>仅当前文件夹笔记列表</summary>
    NoteList,

    /// <summary>仅当前打开笔记（从 DB 重载）</summary>
    CurrentNoteOnly
}

/// <summary>
/// 导航状态与 DB 读模型的一致性边界：由 <see cref="INavigationPresentationCoordinator"/> 驱动刷新。
/// </summary>
public interface INavigationStore
{
    /// <summary>当前选中文件夹 Id</summary>
    Guid? SelectedFolderId { get; }

    /// <summary>当前选中笔记 Id</summary>
    Guid? SelectedNoteId { get; }

    /// <summary>按 Id 选中笔记（树模式/列表模式均从 NoteList.Notes 解析，避免引用不一致）</summary>
    bool SelectNoteById(Guid noteId, Guid folderId);

    /// <summary>刷新导航面板</summary>
    void Refresh(NavigationRefreshScope scope = NavigationRefreshScope.All);

    /// <summary>读模型变更：增量 patch 导航 VM，失败时 fallback 全量 Refresh</summary>
    void ApplyRemoteDelta(SyncNavigationDelta delta, bool isTreeMode);

    /// <summary>搜索过滤变化：仅重建过滤视图，不触碰编辑器</summary>
    void ApplyFilterProjection(bool isTreeMode);

    /// <summary>布局/模式切换：全量重建导航树与列表</summary>
    void ApplyLayoutRebuild(bool isTreeMode);

    /// <summary>笔记即将切换（供 MainWindow 设置 _isLoadingNote）</summary>
    event Action<LocalNote>? NoteSwitching;

    /// <summary>无笔记选中</summary>
    event Action? NoteClosed;
}
