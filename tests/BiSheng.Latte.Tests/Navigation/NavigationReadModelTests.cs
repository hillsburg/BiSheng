using BiSheng.Latte.Services.Navigation;
using BiSheng.Shared;
using Xunit;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>NavigationReadModel：投影更新发布</summary>
public class NavigationReadModelTests
{
    /// <summary>Publish 应触发 Changed 订阅者</summary>
    [Fact]
    public void Publish_InvokesChangedSubscribers()
    {
        var model = new NavigationReadModel();
        NavigationProjectionUpdate? received = null;
        model.Changed += update => received = update;

        var delta = SyncNavigationDelta.FromChanges(new[]
        {
            new NavigationChange
            {
                EntityType = EntityTypes.Note,
                EntityId = Guid.NewGuid(),
                Action = ChangeActions.Update
            }
        });

        model.Publish(NavigationProjectionUpdate.FromDelta(delta), waitForPresentation: true);

        Assert.NotNull(received);
        Assert.Equal(NavigationProjectionKind.DataChange, received!.Kind);
        Assert.Same(delta, received.Delta);
    }

    /// <summary>Filter 投影可独立发布</summary>
    [Fact]
    public void Publish_FilterProjection_SetsKind()
    {
        var model = new NavigationReadModel();
        NavigationProjectionKind? kind = null;
        model.Changed += update => kind = update.Kind;

        model.Publish(NavigationProjectionUpdate.Filter, waitForPresentation: true);

        Assert.Equal(NavigationProjectionKind.FilterOnly, kind);
    }
}
