using BiSheng.Latte.Services.Navigation;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>INavigationFilterState：搜索筛选状态与事件</summary>
public class NavigationFilterStateTests
{
    /// <summary>空搜索时 IsSearchActive 为 false</summary>
    [Fact]
    public void IsSearchActive_EmptyText_ReturnsFalse()
    {
        var state = new NavigationFilterState();

        Assert.False(state.IsSearchActive);
    }

    /// <summary>非空搜索时 IsSearchActive 为 true</summary>
    [Fact]
    public void IsSearchActive_NonEmptyText_ReturnsTrue()
    {
        var state = new NavigationFilterState { SearchText = "hello" };

        Assert.True(state.IsSearchActive);
    }

    /// <summary>相同值赋值不触发 FilterChanged</summary>
    [Fact]
    public void SearchText_SameValue_DoesNotRaiseFilterChanged()
    {
        var state = new NavigationFilterState { SearchText = "a" };
        var raised = 0;
        state.FilterChanged += () => raised++;

        state.SearchText = "a";

        Assert.Equal(0, raised);
    }

    /// <summary>变更搜索词触发 FilterChanged</summary>
    [Fact]
    public void SearchText_Changed_RaisesFilterChanged()
    {
        var state = new NavigationFilterState();
        var raised = 0;
        state.FilterChanged += () => raised++;

        state.SearchText = "query";

        Assert.Equal(1, raised);
        Assert.Equal("query", state.SearchText);
    }
}
