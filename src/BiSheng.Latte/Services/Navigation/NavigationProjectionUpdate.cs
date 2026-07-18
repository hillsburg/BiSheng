namespace BiSheng.Latte.Services.Navigation;

/// <summary>导航投影更新类型</summary>
public enum NavigationProjectionKind
{
    /// <summary>数据变更（远端同步或本地 CRUD）</summary>
    DataChange,

    /// <summary>仅搜索过滤变化，不触碰编辑器</summary>
    FilterOnly,

    /// <summary>布局/模式切换等结构性重建</summary>
    LayoutRebuild
}

/// <summary>读模型向展示层发布的单次更新</summary>
public sealed class NavigationProjectionUpdate
{
    /// <summary>无数据变更</summary>
    public static NavigationProjectionUpdate Empty { get; } =
        new() { Kind = NavigationProjectionKind.DataChange, Delta = SyncNavigationDelta.Empty };

    /// <summary>全量数据重建</summary>
    public static NavigationProjectionUpdate FullDataRebuild { get; } =
        new() { Kind = NavigationProjectionKind.DataChange, Delta = SyncNavigationDelta.FullRefresh };

    /// <summary>仅过滤</summary>
    public static NavigationProjectionUpdate Filter { get; } =
        new() { Kind = NavigationProjectionKind.FilterOnly };

    /// <summary>布局重建</summary>
    public static NavigationProjectionUpdate Layout { get; } =
        new() { Kind = NavigationProjectionKind.LayoutRebuild };

    /// <summary>更新类型</summary>
    public NavigationProjectionKind Kind { get; init; }

    /// <summary>数据增量（FilterOnly / LayoutRebuild 时可为 Empty）</summary>
    public SyncNavigationDelta Delta { get; init; } = SyncNavigationDelta.Empty;

    /// <summary>从数据增量构造</summary>
    public static NavigationProjectionUpdate FromDelta(SyncNavigationDelta delta) =>
        new() { Kind = NavigationProjectionKind.DataChange, Delta = delta };
}
