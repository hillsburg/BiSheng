using System.Windows;
using BiSheng.Latte.Views;

namespace BiSheng.Latte.Services;

/// <summary>
/// 统一弹窗入口：替代 MessageBox.Show，样式与当前主题一致。
/// </summary>
public static class AppDialog
{
    /// <summary>显示弹窗（自动选取主窗口为 Owner）</summary>
    public static MessageBoxResult Show(
        string message,
        string title = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        return Show(ResolveOwner(), message, title, button, icon);
    }

    /// <summary>显示弹窗并指定 Owner</summary>
    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title = "",
        MessageBoxButton button = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None)
    {
        var dialog = new AppDialogWindow(message, title, button, icon)
        {
            Owner = owner ?? ResolveOwner(),
        };

        dialog.ShowDialog();
        return dialog.Result;
    }

    /// <summary>信息提示</summary>
    public static void Info(string message, string title = "提示") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>信息提示（指定 Owner）</summary>
    public static void Info(Window owner, string message, string title = "提示") =>
        Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>成功提示</summary>
    public static void Success(string message, string title = "成功") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    /// <summary>错误提示</summary>
    public static void Error(string message, string title = "错误") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>错误提示（指定 Owner）</summary>
    public static void Error(Window owner, string message, string title = "错误") =>
        Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    /// <summary>警告提示</summary>
    public static void Warning(string message, string title = "警告") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    /// <summary>是/否确认，返回是否选择「是」</summary>
    public static bool Confirm(string message, string title = "确认") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>是/否确认（指定 Owner）</summary>
    public static bool Confirm(Window owner, string message, string title = "确认") =>
        Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>危险操作确认（警告样式 + 红色确认按钮）</summary>
    public static bool ConfirmDanger(string message, string title = "确认") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>危险操作确认（指定 Owner）</summary>
    public static bool ConfirmDanger(Window owner, string message, string title = "确认") =>
        Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>解析弹窗 Owner：优先主窗口，其次当前活动窗口</summary>
    private static Window? ResolveOwner()
    {
        var app = Application.Current;
        if (app == null)
        {
            return null;
        }

        if (app.MainWindow?.IsVisible == true)
        {
            return app.MainWindow;
        }

        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
    }
}
