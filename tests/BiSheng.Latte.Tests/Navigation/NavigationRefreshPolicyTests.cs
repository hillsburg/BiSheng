using BiSheng.Latte.Services.Navigation;
using BiSheng.Shared;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>NavigationRefreshPolicy：增量 vs 全量 fallback 条件</summary>
public class NavigationRefreshPolicyTests
{
    /// <summary>RequiresFullRefresh 时必走全量</summary>
    [Fact]
    public void ShouldUseFullRefresh_WhenDeltaRequiresFullRefresh()
    {
        var delta = SyncNavigationDelta.FullRefresh;

        Assert.True(NavigationRefreshPolicy.ShouldUseFullRefresh(delta, isSearchActive: false, isRenaming: false));
    }

    /// <summary>搜索激活时不做增量</summary>
    [Fact]
    public void ShouldUseFullRefresh_WhenSearchActive()
    {
        var delta = SyncNavigationDelta.FromChanges(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Update
            }
        });

        Assert.True(NavigationRefreshPolicy.ShouldUseFullRefresh(delta, isSearchActive: true, isRenaming: false));
    }

    /// <summary>内联重命名时不做增量</summary>
    [Fact]
    public void ShouldUseFullRefresh_WhenRenaming()
    {
        var delta = SyncNavigationDelta.FromChanges(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Folder,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Update
            }
        });

        Assert.True(NavigationRefreshPolicy.ShouldUseFullRefresh(delta, isSearchActive: false, isRenaming: true));
    }

    /// <summary>Folder 父级变化时需全量</summary>
    [Fact]
    public void ShouldUseFullRefresh_WhenParentFolderChanged()
    {
        var delta = SyncNavigationDelta.FromChanges(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Folder,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Update,
                ParentFolderChanged = true
            }
        });

        Assert.True(NavigationRefreshPolicy.ShouldUseFullRefresh(delta, isSearchActive: false, isRenaming: false));
    }

    /// <summary>小批量无特殊条件时可增量</summary>
    [Fact]
    public void ShouldNotUseFullRefresh_ForSmallBatch()
    {
        var delta = SyncNavigationDelta.FromChanges(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Update,
                FolderId = Guid.NewGuid()
            }
        });

        Assert.False(NavigationRefreshPolicy.ShouldUseFullRefresh(delta, isSearchActive: false, isRenaming: false));
    }
}
