namespace BiSheng.Latte.Services.Navigation;

/// <summary>判断同步后是否应 fallback 全量 Refresh</summary>
public static class NavigationRefreshPolicy
{
    /// <summary>超过此数量则全量 Refresh</summary>
    public const int MaxIncrementalChanges = 50;

    /// <summary>是否应使用全量 Refresh</summary>
    public static bool ShouldUseFullRefresh(
        SyncNavigationDelta delta,
        bool isSearchActive,
        bool isRenaming)
    {
        if (delta.RequiresFullRefresh)
        {
            return true;
        }

        if (isSearchActive)
        {
            return true;
        }

        if (isRenaming)
        {
            return true;
        }

        if (delta.Changes.Count > MaxIncrementalChanges)
        {
            return true;
        }

        if (delta.Changes.Any(c => c.ParentFolderChanged))
        {
            return true;
        }

        return false;
    }
}
