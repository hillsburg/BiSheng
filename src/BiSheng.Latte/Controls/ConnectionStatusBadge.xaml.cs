using System.Windows;
using System.Windows.Controls;

namespace BiSheng.Latte.Controls;

/// <summary>工具栏连接 / 同步状态徽章（顶栏完整、侧栏紧凑；点击走统一连接入口）</summary>
public partial class ConnectionStatusBadge : UserControl
{
    /// <summary>紧凑模式：仅图标，文字进 Tooltip</summary>
    public static readonly DependencyProperty CompactProperty =
        DependencyProperty.Register(
            nameof(Compact),
            typeof(bool),
            typeof(ConnectionStatusBadge),
            new PropertyMetadata(false));

    /// <summary>是否紧凑显示</summary>
    public bool Compact
    {
        get => (bool)GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>构造连接状态徽章</summary>
    public ConnectionStatusBadge()
    {
        InitializeComponent();
    }
}
