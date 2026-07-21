using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BiSheng.Latte.Composition;
using BiSheng.Latte.Helpers;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using BiSheng.Latte.ViewModels;
using BiSheng.Latte.Views;
using Microsoft.Win32;

namespace BiSheng.Latte;

/// <summary>主窗口：View 层壳，业务状态与命令由 MainViewModel 驱动</summary>
public partial class MainWindow : Window, IMainWindowHost
{
    private readonly MainViewModel _vm;
    private TrayIconService? _tray;
    private UserPreferenceChangedEventHandler? _themeWatcher;
    private bool _shutdownInProgress;
    private bool _shutdownReady;
    private bool _forceExit;
    private bool _trayHintShown;

    public MainWindow(ViewModels.MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = _vm;

        Loaded += OnLoaded;
        Closing += OnClosing;
        Activated += OnActivated;
        StateChanged += OnWindowStateChanged;
    }

    /// <summary>由 App 注入托盘服务</summary>
    public void AttachTray(TrayIconService tray)
    {
        _tray = tray;
        _tray.OpenRequested += ShowFromTray;
        _tray.ExitRequested += ExitApplication;
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_shutdownReady)
        {
            return;
        }

        e.Cancel = true;

        if (_shutdownInProgress)
        {
            return;
        }

        // 关闭到托盘（非强制退出）
        if (!_forceExit && AppearanceSettings.Load().CloseToTray)
        {
            HideToTray();
            return;
        }

        _shutdownInProgress = true;

        _vm.CaptureLayout().Save();

        if (_themeWatcher != null)
        {
            SystemEvents.UserPreferenceChanged -= _themeWatcher;
            _themeWatcher = null;
        }

        _vm.Editor.ForceSave(NoteEditor.Text, checkpointRevision: true);

        try
        {
            await _vm.RunShutdownAsync();
        }
        catch (Exception ex)
        {
            LogHelper.Error("退出同步失败", ex);
        }

        // 关闭同步引擎后再备份，便于 VACUUM 获取数据库独占访问
        LocalDatabaseBackupService.TryRunScheduledBackup(DataSafetySettings.Load(), onExit: true);

        _tray?.Dispose();
        _tray = null;

        // Closing 回调内不能同步 Close()；延后到当前关闭流程结束后再关窗并退出应用
        _shutdownReady = true;
        Closing -= OnClosing;
        _ = Dispatcher.BeginInvoke(() =>
        {
            Close();
            Application.Current?.Shutdown();
        });
    }

    private void HideToTray()
    {
        _vm.CaptureLayout().Save();
        try
        {
            _vm.Editor.ForceSave(NoteEditor.Text, checkpointRevision: false);
        }
        catch (Exception ex)
        {
            LogHelper.Warn("隐藏到托盘前保存笔记失败: {0}", ex.Message);
        }

        Hide();
        ShowInTaskbar = false;

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _tray?.NotifyMinimizedToTray();
        }
    }

    /// <summary>从托盘恢复主窗口</summary>
    public void ShowFromTray()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>强制完整退出（托盘菜单 / 更新前）</summary>
    public void ExitApplication()
    {
        _forceExit = true;
        if (!IsVisible)
        {
            ShowInTaskbar = true;
            Show();
        }

        Close();
    }

    /// <summary>更新前：落盘布局与编辑器，释放托盘（同步已由关于页 flush）</summary>
    public void PrepareForUpdateRestart()
    {
        _forceExit = true;
        try
        {
            _vm.CaptureLayout().Save();
            _vm.Editor.ForceSave(NoteEditor.Text, checkpointRevision: true);
        }
        catch (Exception ex)
        {
            LogHelper.Error("更新前保存失败", ex);
        }

        LocalDatabaseBackupService.TryRunScheduledBackup(DataSafetySettings.Load(), onExit: true);
        _tray?.Dispose();
        _tray = null;
    }

    /// <summary>应用从后台唤醒时触发同步补偿</summary>
    private void OnActivated(object? sender, EventArgs e)
    {
        _vm.SyncEngine.OnAppActivated();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.WindowHost = this;

        ApplyAppearanceSettings();
        var layout = LayoutSettings.Load();
        var deferRestore = _vm.IsConnected && !_vm.AuthService.IsOfflineMode;
        _vm.ApplyLayout(layout, restoreSelection: !deferRestore);
        _vm.UpdateConnectionStatus();

        _themeWatcher = (_, _) => Dispatcher.Invoke(() =>
        {
            var settings = AppearanceSettings.Load();
            if (settings.ActiveTheme == "System")
            {
                ApplyAppearanceSettings();
            }
        });
        SystemEvents.UserPreferenceChanged += _themeWatcher;

        InitializeEditorHost();
        _vm.EditorTextProvider = () => NoteEditor.Text;

        _vm.SyncEngine.OnConflictsDetected += count =>
        {
            Dispatcher.Invoke(() => _vm.SetConflictCount(count));
        };

        var initialCount = _vm.SyncEngine.GetUnresolvedConflictCount();
        if (initialCount > 0)
        {
            _vm.SetConflictCount(initialCount);
        }

        _vm.OutlineRefreshRequested += RefreshOutline;

        await _vm.RunStartupAsync(layout);
        MaybePromptIntegrityFailure();
        MaybePromptFullExportReminder();
    }

    private void MaybePromptIntegrityFailure()
    {
        if (!App.StartupIntegrityFailed)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            BackupManagerHost.PromptIntegrityFailure(this, App.StartupIntegrityMessage);
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void MaybePromptFullExportReminder()
    {
        var settings = DataSafetySettings.Load();
        if (!settings.ShouldRemindExport())
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            var result = AppDialog.Show(
                $"已超过 {settings.RemindExportAfterDays} 天未导出全库备份。\n" +
                "建议使用工具栏「导出全部库」将笔记与图片保存为 BiSheng Archive（可放入网盘）。",
                "数据安全提醒",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                _vm.ExportFullLibraryCommand.Execute(null);
            }
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        _vm.UpdateMaximizeState(WindowState);
    }

    /// <summary>拖拽工具栏移动窗口，双击切换最大化</summary>
    private void OnToolbarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Button)
            return;

        WindowChromeHelper.HandleDragOrMaximize(this, e, ToggleMaximize);
    }

    void IMainWindowHost.Minimize() => WindowState = WindowState.Minimized;

    void IMainWindowHost.ToggleMaximize() => ToggleMaximize();

    void IMainWindowHost.Close() => Close();

    void IMainWindowHost.ExitApplication() => ExitApplication();

    void IMainWindowHost.ApplyAppearanceSettings() => ApplyAppearanceSettings();

    bool? IMainWindowHost.ShowSyncSettingsDialog()
    {
        return new SyncSettingsWindow(
            _vm.AuthService,
            onSyncEnabledChanged: async () =>
            {
                // 仅刷新状态；引擎重启交给对话框 DialogResult=true 后的 DialogNavigationService，
                // 或下方 Cancel 后的补偿重启（避免「开关同步 + 确定」双重 Restart）
                _vm.UpdateConnectionStatus();
            },
            refreshConnectionStatus: () => _vm.UpdateConnectionStatus(),
            onDismissWithoutSaveAfterToggle: async () =>
            {
                _vm.UpdateConnectionStatus();
                await _vm.RestartSyncEngineAsync();
            },
            getPendingChangeCount: () => _vm.GetPendingChangeCount())
        { Owner = this }.ShowDialog();
    }

    bool? IMainWindowHost.ShowAppearanceDialog()
    {
        return new AppearanceSettingsWindow
        {
            Owner = this,
            ApplyCallback = settings => ApplyAppearanceSettings(settings)
        }.ShowDialog();
    }

    bool? IMainWindowHost.ShowConflictDialog()
    {
        try
        {
            return new ConflictResolutionDialog(_vm.SyncEngine) { Owner = this }.ShowDialog();
        }
        catch (Exception ex)
        {
            LogHelper.Error("打开冲突解决对话框失败", ex);
            AppDialog.Error($"无法打开冲突解决窗口：\n{ex.Message}", "冲突解决");
            return false;
        }
    }

    void IMainWindowHost.ShowBackupManagerDialog() =>
        BackupManagerHost.ShowManager(this);

    void IMainWindowHost.ShowAboutDialog()
    {
        var dialog = new AboutWindow(
            LatteHost.GetRequiredService<AppUpdateService>(),
            LatteHost.GetRequiredService<SyncService>(),
            LatteHost.GetRequiredService<ImageSyncService>(),
            prepareForUpdateRestart: PrepareForUpdateRestart)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }
}
