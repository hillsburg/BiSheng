using System.Collections.ObjectModel;
using BiSheng.Latte.Services.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiSheng.Latte.ViewModels;

/// <summary>导航区共享状态：收藏快捷列表；搜索绑定 <see cref="INavigationFilterState"/></summary>
public partial class NavigationViewModel : ObservableObject
{
    private readonly INavigationFilterState _filterState;

    /// <summary>收藏/置顶快捷项</summary>
    public ObservableCollection<object> FavoriteItems { get; } = new();

    /// <summary>搜索框草稿（按 Enter 后才提交为筛选）</summary>
    [ObservableProperty]
    private string _searchInput = string.Empty;

    /// <summary>构造导航 VM</summary>
    public NavigationViewModel(INavigationFilterState filterState)
    {
        _filterState = filterState;
        _filterState.FilterChanged += () =>
        {
            SearchInput = _filterState.SearchText;
            OnPropertyChanged(nameof(SearchText));
        };
    }

    /// <summary>已生效的搜索关键词（只读，来自 FilterState）</summary>
    public string SearchText => _filterState.SearchText;

    /// <summary>将搜索框内容提交为筛选条件</summary>
    [RelayCommand]
    private void ApplySearch()
    {
        var normalized = SearchInput?.Trim() ?? string.Empty;
        if (_filterState.SearchText == normalized)
        {
            return;
        }

        _filterState.SearchText = normalized;
        OnPropertyChanged(nameof(SearchText));
    }

    /// <summary>清空搜索筛选（定位笔记等场景需展示完整导航树）</summary>
    public void ClearSearchFilter()
    {
        SearchInput = string.Empty;
        if (string.IsNullOrEmpty(_filterState.SearchText))
        {
            return;
        }

        _filterState.SearchText = string.Empty;
        OnPropertyChanged(nameof(SearchText));
    }

    [ObservableProperty]
    private bool _isFavoritesExpanded = true;

    /// <summary>是否有收藏项（控制收藏区显隐）</summary>
    [ObservableProperty]
    private bool _hasFavorites;
}
