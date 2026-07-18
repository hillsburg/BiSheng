using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>将读模型增量应用到导航 VM；失败时 caller 应 fallback 全量 Refresh</summary>
public static class NavigationPatcher
{
    /// <summary>尝试增量 patch；返回 false 表示应全量 Refresh</summary>
    public static bool TryApplyIncremental(
        SyncNavigationDelta delta,
        FolderTreeViewModel folderTree,
        NoteListViewModel noteList,
        INavigationFilterState filterState,
        bool isTreeMode,
        bool isRenaming)
    {
        if (NavigationRefreshPolicy.ShouldUseFullRefresh(delta, filterState.IsSearchActive, isRenaming))
        {
            return false;
        }

        if (delta.Changes.Count == 0)
        {
            return true;
        }

        if (!folderTree.ApplyNavigationDelta(delta.Changes, filterState.IsSearchActive))
        {
            return false;
        }

        if (!isTreeMode && !noteList.ApplyNavigationDelta(delta.Changes))
        {
            return false;
        }

        return true;
    }
}
