using System.Windows;
using BiSheng.Latte.Models;

namespace BiSheng.Latte;

/// <summary>状态栏固定显示 / 隐藏行为</summary>
public partial class MainWindow
{
    /// <summary>根据外观设置应用状态栏可见性</summary>
    internal void ApplyStatusBarVisibilityBehavior(StatusBarVisibilityMode mode)
    {
        if (mode == StatusBarVisibilityMode.Hidden)
        {
            StatusBarBorder.Visibility = Visibility.Collapsed;
            return;
        }

        StatusBarBorder.Visibility = Visibility.Visible;
        StatusBarBorder.Opacity = 1;
    }
}
