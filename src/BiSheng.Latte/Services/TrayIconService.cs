using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace BiSheng.Latte.Services;

/// <summary>
/// 系统托盘图标：打开主窗口 / 退出应用
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private TaskbarIcon? _icon;
    private bool _disposed;

    /// <summary>双击或菜单「打开」</summary>
    public event Action? OpenRequested;

    /// <summary>菜单「退出」——须走完整退出流程</summary>
    public event Action? ExitRequested;

    /// <summary>创建并显示托盘图标</summary>
    public void Initialize()
    {
        if (_icon != null)
        {
            return;
        }

        var menu = new ContextMenu();
        var openItem = new MenuItem { Header = "打开 Latte" };
        openItem.Click += (_, _) => OpenRequested?.Invoke();
        var exitItem = new MenuItem { Header = "退出" };
        exitItem.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _icon = new TaskbarIcon
        {
            ToolTipText = "BiSheng Latte",
            ContextMenu = menu,
            Visibility = Visibility.Visible
        };

        try
        {
            _icon.Icon = LoadAppIcon();
        }
        catch (Exception ex)
        {
            LogHelper.Warn("加载托盘图标失败，将使用默认图标: {0}", ex.Message);
        }

        _icon.TrayMouseDoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    /// <summary>窗口已隐藏到托盘时可选气泡提示（仅首次短暂显示）</summary>
    public void NotifyMinimizedToTray()
    {
        try
        {
            _icon?.ShowBalloonTip(
                "BiSheng Latte",
                "已最小化到托盘。双击图标可重新打开，右键可退出。",
                BalloonIcon.Info);
        }
        catch
        {
            /* 气泡失败不影响托盘 */
        }
    }

    private static Icon LoadAppIcon()
    {
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "img", "latte.icon.ico");
        if (File.Exists(icoPath))
        {
            return new Icon(icoPath);
        }

        // 打包后 ApplicationIcon 通常在 exe 旁无独立 ico；从 WPF Resource 读取
        var uri = new Uri("pack://application:,,,/Resources/img/latte.icon.ico", UriKind.Absolute);
        var streamInfo = Application.GetResourceStream(uri);
        if (streamInfo?.Stream != null)
        {
            return new Icon(streamInfo.Stream);
        }

        // 最后回退：从当前进程主模块提取
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
        {
            return Icon.ExtractAssociatedIcon(exe)
                ?? SystemIcons.Application;
        }

        return SystemIcons.Application;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_icon != null)
        {
            _icon.Dispose();
            _icon = null;
        }
    }
}
