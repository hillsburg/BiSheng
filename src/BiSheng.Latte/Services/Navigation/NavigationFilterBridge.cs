namespace BiSheng.Latte.Services.Navigation;

/// <summary>筛选状态 → 读模型 FilterOnly 投影</summary>
public sealed class NavigationFilterBridge
{
    /// <summary>订阅 INavigationFilterState.FilterChanged</summary>
    public NavigationFilterBridge(
        INavigationFilterState filterState,
        INavigationMutationPublisher mutationPublisher)
    {
        filterState.FilterChanged += () => mutationPublisher.NotifyFilterChanged();
    }
}
