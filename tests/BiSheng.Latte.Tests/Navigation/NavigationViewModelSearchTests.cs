using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.ViewModels;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>NavigationViewModel：搜索框 Enter 提交</summary>
public class NavigationViewModelSearchTests
{
    /// <summary>输入草稿不立即生效，ApplySearch 后才写入 FilterState</summary>
    [Fact]
    public void ApplySearch_CommitsTrimmedInputToFilterState()
    {
        var filterState = new NavigationFilterState();
        var nav = new NavigationViewModel(filterState);
        var changed = 0;
        filterState.FilterChanged += () => changed++;

        nav.SearchInput = "  hello  ";
        Assert.Equal(string.Empty, filterState.SearchText);
        Assert.Equal(0, changed);

        nav.ApplySearchCommand.Execute(null);

        Assert.Equal("hello", filterState.SearchText);
        Assert.Equal("hello", nav.SearchText);
        Assert.Equal(1, changed);
    }

    /// <summary>空内容 Enter 清除筛选</summary>
    [Fact]
    public void ApplySearch_EmptyInput_ClearsFilter()
    {
        var filterState = new NavigationFilterState { SearchText = "old" };
        var nav = new NavigationViewModel(filterState);

        nav.SearchInput = string.Empty;
        nav.ApplySearchCommand.Execute(null);

        Assert.Equal(string.Empty, filterState.SearchText);
        Assert.False(filterState.IsSearchActive);
    }
}
