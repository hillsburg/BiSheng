using BiSheng.Latte.Services.Navigation;

namespace BiSheng.Latte.Tests.Navigation;

/// <summary>测试用导航变更发布栈（无 Coordinator 时不应用 UI）</summary>
internal static class NavigationTestPublisher
{
    /// <summary>创建读模型 + 发布器</summary>
    public static (INavigationReadModel ReadModel, INavigationMutationPublisher Publisher) Create()
    {
        var readModel = new NavigationReadModel();
        var publisher = new NavigationMutationPublisher(readModel);
        return (readModel, publisher);
    }

    /// <summary>创建独立筛选状态（测试 VM 构造）</summary>
    public static INavigationFilterState CreateFilterState() => new NavigationFilterState();
}
