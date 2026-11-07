using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using BiSheng.Latte.Models;

namespace BiSheng.Latte.Views;

/// <summary>
/// 外观设置 · 布局页：导航布局、工具栏位置与显示方式、状态栏、关闭到托盘。
/// 每次选择都立即回调宿主预览，不落盘；保存 / 应用时才持久化。
/// </summary>
public partial class AppearanceSettingsWindow
{
    private void OnSideBySideSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.LayoutMode = NavigationLayoutMode.SideBySide;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnTreeViewSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.LayoutMode = NavigationLayoutMode.TreeView;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarTopSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarPlacement = ToolbarPlacement.Top;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarNavLeftSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarPlacement = ToolbarPlacement.NavLeft;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarFixedSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarVisibilityMode = ToolbarVisibilityMode.Fixed;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarAutoHideSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarVisibilityMode = ToolbarVisibilityMode.AutoHide;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnStatusBarFixedSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.StatusBarVisibilityMode = StatusBarVisibilityMode.Fixed;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnStatusBarHiddenSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.StatusBarVisibilityMode = StatusBarVisibilityMode.Hidden;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnCloseToTrayChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.CloseToTray = CloseToTrayBox.IsChecked == true;
        ApplyCallback?.Invoke(_settings);
    }

    /// <summary>按当前设置高亮各组中被选中的卡片（强调色边框），其余用浅边框</summary>
    private void UpdateLayoutCards()
    {
        var accentBrush = (Brush)FindResource("Brush.Accent");
        var borderLightBrush = (Brush)FindResource("Brush.BorderLight");

        if (_settings.LayoutMode == NavigationLayoutMode.SideBySide)
        {
            SideBySideCard.BorderBrush = accentBrush;
            TreeViewCard.BorderBrush = borderLightBrush;
        }
        else
        {
            SideBySideCard.BorderBrush = borderLightBrush;
            TreeViewCard.BorderBrush = accentBrush;
        }

        if (_settings.ToolbarPlacement == ToolbarPlacement.Top)
        {
            ToolbarTopCard.BorderBrush = accentBrush;
            ToolbarNavLeftCard.BorderBrush = borderLightBrush;
        }
        else
        {
            ToolbarTopCard.BorderBrush = borderLightBrush;
            ToolbarNavLeftCard.BorderBrush = accentBrush;
        }

        if (_settings.ToolbarVisibilityMode == ToolbarVisibilityMode.Fixed)
        {
            ToolbarFixedCard.BorderBrush = accentBrush;
            ToolbarAutoHideCard.BorderBrush = borderLightBrush;
        }
        else
        {
            ToolbarFixedCard.BorderBrush = borderLightBrush;
            ToolbarAutoHideCard.BorderBrush = accentBrush;
        }

        if (_settings.StatusBarVisibilityMode == StatusBarVisibilityMode.Fixed)
        {
            StatusBarFixedCard.BorderBrush = accentBrush;
            StatusBarHiddenCard.BorderBrush = borderLightBrush;
        }
        else
        {
            StatusBarFixedCard.BorderBrush = borderLightBrush;
            StatusBarHiddenCard.BorderBrush = accentBrush;
        }
    }
}
