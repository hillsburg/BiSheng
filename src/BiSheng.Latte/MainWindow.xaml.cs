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
    private UserPreferenceChangedEventHandler? _themeWatcher;
    private bool _shutdownInProgress;
    private bool _shutdownReady;

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

        _shutdownInProgress = true;

        _vm.CaptureLayout().Save();

        if (_themeWatcher != null)
        {
            SystemEvents.UserPreferenceChanged -= _themeWatcher;
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

        // Closing 回调内不能同步 Close()；延后到当前关闭流程结束后再关窗
        _shutdownReady = true;
        Closing -= OnClosing;
        _ = Dispatcher.BeginInvoke(Close);
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
        return new ConflictResolutionDialog(_vm.SyncEngine) { Owner = this }.ShowDialog();
    }

    void IMainWindowHost.ShowBackupManagerDialog() =>
        BackupManagerHost.ShowManager(this);

    void IMainWindowHost.ShowAboutDialog()
    {
        var dialog = new AboutWindow(
            LatteHost.GetRequiredService<AppUpdateService>(),
            LatteHost.GetRequiredService<SyncService>(),
            LatteHost.GetRequiredService<ImageSyncService>())
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
