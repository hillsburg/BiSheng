using System.Windows;
using System.Windows.Input;

namespace BiSheng.Latte.Helpers;

/// <summary>无边框窗口：拖拽移动 / 双击最大化</summary>
internal static class WindowChromeHelper
{
    public static void HandleDragOrMaximize(Window window, MouseButtonEventArgs e, Action toggleMaximize)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (e.ClickCount == 2)
            toggleMaximize();
        else
            window.DragMove();
    }
}
