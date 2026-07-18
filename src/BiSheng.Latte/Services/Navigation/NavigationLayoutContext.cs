namespace BiSheng.Latte.Services.Navigation;

/// <summary>运行时导航布局模式状态（并列 / 归纳）</summary>
public sealed class NavigationLayoutContext : INavigationLayoutMode
{
    /// <inheritdoc />
    public bool IsTreeMode { get; set; }
}
