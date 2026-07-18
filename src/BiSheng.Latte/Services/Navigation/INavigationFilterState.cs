namespace BiSheng.Latte.Services.Navigation;

/// <summary>导航搜索/筛选状态（与收藏区等 NavigationViewModel 职责分离）</summary>
public interface INavigationFilterState
{
    /// <summary>搜索关键词（标题过滤）</summary>
    string SearchText { get; set; }

    /// <summary>是否处于搜索/筛选激活态</summary>
    bool IsSearchActive { get; }

    /// <summary>筛选条件变化</summary>
    event Action? FilterChanged;
}
