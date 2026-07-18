using System.Windows;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Views;

/// <summary>关于与检查更新</summary>
public partial class AboutWindow : Window
{
    private readonly AppUpdateService _updates;
    private readonly SyncService _sync;
    private readonly ImageSyncService _imageSync;
    private AppUpdateCheckResult? _pendingUpdate;
    private bool _busy;

    /// <summary>构造关于窗口</summary>
    public AboutWindow(
        AppUpdateService updates,
        SyncService sync,
        ImageSyncService imageSync)
    {
        _updates = updates;
        _sync = sync;
        _imageSync = imageSync;
        InitializeComponent();
        VersionText.Text = $"版本 {_updates.GetCurrentVersionDisplay()}";
    }

    /// <summary>关闭</summary>
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>检查更新</summary>
    private async void OnCheckUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        CheckUpdateButton.IsEnabled = false;
        ApplyUpdateButton.Visibility = Visibility.Collapsed;
        ApplyUpdateButton.IsEnabled = false;
        _pendingUpdate = null;
        StatusText.Text = "正在检查更新…";

        try
        {
            var result = await _updates.CheckForUpdatesAsync();
            _pendingUpdate = result;
            StatusText.Text = result.Message;

            if (result.Availability == AppUpdateAvailability.UpdateAvailable)
            {
                ApplyUpdateButton.Visibility = Visibility.Visible;
                ApplyUpdateButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"检查更新失败：{ex.Message}";
            LogHelper.Error("关于页检查更新异常", ex);
        }
        finally
        {
            _busy = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    /// <summary>确认后下载并重启更新</summary>
    private async void OnApplyUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _pendingUpdate?.Availability != AppUpdateAvailability.UpdateAvailable)
        {
            return;
        }

        var confirm = AppDialog.Show(
            this,
            $"{_pendingUpdate.Message}\n\n更新前会尽量同步待推送内容，然后下载并重启应用。是否立即更新？",
            "确认更新",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        CheckUpdateButton.IsEnabled = false;
        ApplyUpdateButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        StatusText.Text = "正在准备更新…";

        try
        {
            StatusText.Text = "正在同步待推送内容…";
            await _sync.StopAsync(flushPending: true);
            await _imageSync.FlushPendingUploadsAsync();

            StatusText.Text = "正在下载更新…";
            var progress = new Progress<int>(p =>
            {
                DownloadProgress.Value = p;
                StatusText.Text = $"正在下载更新… {p}%";
            });

            await _updates.DownloadAndApplyAsync(_pendingUpdate, progress);
            StatusText.Text = "即将重启以完成更新…";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"更新失败：{ex.Message}";
            LogHelper.Error("应用更新失败", ex);
            AppDialog.Error(this, $"更新失败：{ex.Message}", "更新失败");
            _busy = false;
            CheckUpdateButton.IsEnabled = true;
            ApplyUpdateButton.IsEnabled = true;
            DownloadProgress.Visibility = Visibility.Collapsed;
        }
    }
}
