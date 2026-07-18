namespace BiSheng.Latte.Services.Navigation;

/// <summary>导航搜索筛选状态（读模型 FilterOnly 投影的数据源）</summary>
public sealed class NavigationFilterState : INavigationFilterState
{
    private string _searchText = string.Empty;

    /// <inheritdoc />
    public string SearchText
    {
        get => _searchText;
        set
        {
            var normalized = value ?? string.Empty;
            if (_searchText == normalized)
            {
                return;
            }

            _searchText = normalized;
            FilterChanged?.Invoke();
        }
    }

    /// <inheritdoc />
    public bool IsSearchActive => !string.IsNullOrWhiteSpace(_searchText);

    /// <inheritdoc />
    public event Action? FilterChanged;
}
