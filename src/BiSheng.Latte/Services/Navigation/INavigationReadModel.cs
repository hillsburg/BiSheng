namespace BiSheng.Latte.Services.Navigation;

/// <summary>
/// 导航读模型：Sync / 本地 CRUD 写入 SQLite 后仅发布投影变更，UI 订阅此模型。
/// </summary>
public interface INavigationReadModel
{
    /// <summary>发布导航投影更新</summary>
    /// <param name="update">投影描述</param>
    /// <param name="waitForPresentation">true 时在 UI 线程同步投递（本地 CRUD 后续需立即找节点）</param>
    void Publish(NavigationProjectionUpdate update, bool waitForPresentation = false);

    /// <summary>导航投影变更</summary>
    event Action<NavigationProjectionUpdate>? Changed;
}
