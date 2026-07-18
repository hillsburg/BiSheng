using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using Microsoft.Win32;

namespace BiSheng.Latte.Views;

/// <summary>同步行为、连接与本地数据安全设置对话框</summary>
public partial class SyncSettingsWindow : Window
{
    private readonly AuthService _authService;
    private readonly Func<Task>? _onSyncEnabledChanged;
    private readonly Action? _refreshConnectionStatus;
    private readonly Func<Task>? _onDismissWithoutSaveAfterToggle;
    private readonly string _savedServerUrl;
    private readonly string _savedApiKey;
    private DataSafetySettings _dataSafety = null!;
    private bool _suppressSyncToggle;
    private bool _syncEnabledToggled;

    public bool SettingsSaved { get; private set; }

    public SyncSettingsWindow(
        AuthService authService,
        Func<Task>? onSyncEnabledChanged = null,
        Action? refreshConnectionStatus = null,
        int initialTabIndex = 0,
        Func<Task>? onDismissWithoutSaveAfterToggle = null)
    {
        _authService = authService;
        _onSyncEnabledChanged = onSyncEnabledChanged;
        _refreshConnectionStatus = refreshConnectionStatus;
        _onDismissWithoutSaveAfterToggle = onDismissWithoutSaveAfterToggle;
        _savedServerUrl = authService.ServerUrl ?? string.Empty;
        _savedApiKey = authService.ApiKey ?? string.Empty;
        InitializeComponent();
        ProfileCombo.ItemsSource = new[]
        {
            new ProfileOption(DataSafetyProfile.Balanced, "标准（默认）"),
            new ProfileOption(DataSafetyProfile.Conservative, "保守（更密 Push / 备份 / 历史）")
        };
        ProfileCombo.DisplayMemberPath = nameof(ProfileOption.Label);
        LoadConnectionFields();
        LoadFields(SyncSettings.Load(), DataSafetySettings.Load());
        UpdateConnectionStatusDisplay();
        UpdateSyncEnabledSwitchVisibility();
        MainTabControl.SelectedIndex = Math.Clamp(initialTabIndex, 0, MainTabControl.Items.Count - 1);
    }

    private void LoadConnectionFields()
    {
        _suppressSyncToggle = true;
        ServerUrlBox.Text = _authService.ServerUrl ?? string.Empty;
        ApiKeyBox.Password = _authService.ApiKey ?? string.Empty;
        SyncEnabledSwitch.IsChecked = _authService.IsSyncEnabled;
        _suppressSyncToggle = false;
    }

    private void UpdateConnectionStatusDisplay()
    {
        var display = ConnectionDisplayResolver.Resolve(_authService, SyncStatus.Idle, hasConflicts: false);
        ConnectionStatusText.Text = display.DetailText;
        ConnectionStatusText.Foreground = TryFindResource(display.BrushKey) as Brush
            ?? TryFindResource(ThemeBrushKeys.TextMuted) as Brush
            ?? Brushes.Gray;
    }

    private void UpdateSyncEnabledSwitchVisibility()
    {
        var hasCredentialFields =
            !string.IsNullOrWhiteSpace(ServerUrlBox.Text)
            && !string.IsNullOrWhiteSpace(ApiKeyBox.Password);

        SyncEnabledSwitch.Visibility = hasCredentialFields
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!hasCredentialFields)
        {
            _suppressSyncToggle = true;
            SyncEnabledSwitch.IsChecked = false;
            _suppressSyncToggle = false;
        }
    }

    private void OnConnectionFieldChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSyncEnabledSwitchVisibility();
    }

    private void OnApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateSyncEnabledSwitchVisibility();
    }

    private async void OnSyncEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSyncToggle)
        {
            return;
        }

        var enabled = SyncEnabledSwitch.IsChecked == true;

        if (enabled)
        {
            if (!TryApplyCredentialsFromFields(out var error))
            {
                _suppressSyncToggle = true;
                SyncEnabledSwitch.IsChecked = false;
                _suppressSyncToggle = false;
                ShowStatus(error, ThemeBrushKeys.Danger);
                return;
            }
        }

        _authService.IsSyncEnabled = enabled;
        try
        {
            _authService.SaveConfig();
        }
        catch (InvalidOperationException ex)
        {
            _suppressSyncToggle = true;
            SyncEnabledSwitch.IsChecked = !enabled;
            _authService.IsSyncEnabled = !enabled;
            _suppressSyncToggle = false;
            ShowStatus(ex.Message, ThemeBrushKeys.Danger);
            return;
        }

        _syncEnabledToggled = true;
        UpdateConnectionStatusDisplay();
        ShowStatus(
            enabled ? "已启用同步" : "已关闭同步",
            enabled ? ThemeBrushKeys.Success : ThemeBrushKeys.TextMuted);

        if (_onSyncEnabledChanged != null)
        {
            await _onSyncEnabledChanged();
        }

        UpdateConnectionStatusDisplay();
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        var serverUrl = ServerUrlBox.Text.Trim();
        var apiKey = ApiKeyBox.Password.Trim();

        if (string.IsNullOrEmpty(serverUrl))
        {
            ShowStatus("请填入服务器地址", ThemeBrushKeys.Danger);
            return;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            ShowStatus("请填入 API Key", ThemeBrushKeys.Danger);
            return;
        }

        ShowStatus("正在连接...", ThemeBrushKeys.TextMuted);
        TestConnectionButton.IsEnabled = false;

        var result = await AuthService.ProbeConnectionAsync(serverUrl, apiKey);
        var testingSavedCredentials = IsTestingSavedCredentials(serverUrl, apiKey);

        TestConnectionButton.IsEnabled = true;

        if (result.Success)
        {
            if (testingSavedCredentials)
            {
                _authService.Username = result.Username;
                _authService.SetServerVerified(true);
                _refreshConnectionStatus?.Invoke();
            }

            ShowStatus(
                testingSavedCredentials
                    ? $"连接成功！用户: {result.Username ?? "—"}"
                    : $"连接成功（未保存的配置）。用户: {result.Username ?? "—"}",
                ThemeBrushKeys.Success);
        }
        else
        {
            if (testingSavedCredentials)
            {
                _authService.SetServerVerified(false);
                _refreshConnectionStatus?.Invoke();
            }

            ShowStatus(
                testingSavedCredentials
                    ? "连接失败，请检查服务器地址和 API Key"
                    : "连接失败（当前输入尚未保存，不影响已保存配置的状态）",
                ThemeBrushKeys.Danger);
        }

        UpdateConnectionStatusDisplay();
    }

    private bool IsTestingSavedCredentials(string serverUrl, string apiKey) =>
        string.Equals(serverUrl, _savedServerUrl, StringComparison.OrdinalIgnoreCase)
        && string.Equals(apiKey, _savedApiKey, StringComparison.Ordinal);

    private void LoadFields(SyncSettings sync, DataSafetySettings dataSafety)
    {
        _dataSafety = dataSafety;

        PeriodicPushBox.Text = sync.PeriodicPushIntervalSeconds.ToString();
        FlushOnExitBox.IsChecked = sync.FlushOnExit;
        SyncOnActivatedBox.IsChecked = sync.SyncOnAppActivated;
        SyncOnNetworkRecoverBox.IsChecked = sync.SyncOnNetworkRecover;
        ImageUploadBox.Text = sync.ImageUploadIntervalSeconds.ToString();
        ImagePullBox.Text = sync.ImagePullIntervalSeconds.ToString();

        ProfileCombo.SelectedItem = ((ProfileOption[])ProfileCombo.ItemsSource!)
            .FirstOrDefault(p => p.Profile == dataSafety.Profile)
            ?? ProfileCombo.Items[0];
        EnableAutoBackupBox.IsChecked = dataSafety.EnableAutoBackup;
        BackupOnExitBox.IsChecked = dataSafety.BackupOnExit;
        BackupRetentionBox.Text = dataSafety.BackupRetentionCount.ToString();
        BackupIntervalBox.Text = dataSafety.BackupIntervalHours.ToString();
        BackupUseDefaultBox.IsChecked = dataSafety.BackupDirectoryUseDefault;
        BackupCustomPathBox.Text = dataSafety.BackupDirectory;
        BackupDefaultPathText.Text = LocalDatabasePaths.DefaultBackupDirectory;
        UpdateBackupLocationUi();
        UpdateBackupPathHint();

        EnableEditJournalBox.IsChecked = dataSafety.EnableEditJournal;
        EditJournalRetentionBox.Text = dataSafety.EditJournalRetentionDays.ToString();
        TrashRetentionBox.Text = dataSafety.TrashRetentionDays.ToString();
        EnableExportReminderBox.IsChecked = dataSafety.EnableExportReminder;
        RemindExportBox.Text = dataSafety.RemindExportAfterDays.ToString();
        LastExportHint.Text = dataSafety.LastFullExportUtc.HasValue
            ? $"上次全库导出：{dataSafety.LastFullExportUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}"
            : "尚未记录全库导出时间";
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryApplyCredentialsForSave(out var connectionError))
        {
            ShowStatus(connectionError, ThemeBrushKeys.Danger);
            return;
        }

        if (!TryBuildSyncSettings(out var sync, out var syncError))
        {
            ShowStatus(syncError, ThemeBrushKeys.Danger);
            return;
        }

        if (!TryBuildDataSafetySettings(out var dataSafety, out var safetyError))
        {
            ShowStatus(safetyError, ThemeBrushKeys.Danger);
            return;
        }

        if (dataSafety.Profile == DataSafetyProfile.Conservative)
        {
            DataSafetyProfileApplier.ApplyProfile(dataSafety.Profile, sync, dataSafety);
        }

        try
        {
            _authService.SaveConfig();
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(ex.Message, ThemeBrushKeys.Danger);
            return;
        }

        var oldBackupDir = LocalDatabasePaths.ResolveBackupDirectory(_dataSafety);
        dataSafety.LastBackupUtc = _dataSafety.LastBackupUtc;
        dataSafety.LastFullExportUtc = _dataSafety.LastFullExportUtc;
        sync.Save();
        dataSafety.Save();
        var newBackupDir = LocalDatabasePaths.ResolveBackupDirectory(dataSafety);
        if (!string.Equals(oldBackupDir, newBackupDir, StringComparison.OrdinalIgnoreCase))
        {
            LogHelper.Info("备份目录已变更: {0} → {1}", oldBackupDir, newBackupDir);
        }

        UpdateConnectionStatusDisplay();
        SettingsSaved = true;
        DialogResult = true;
        Close();
    }

    private bool TryApplyCredentialsForSave(out string error)
    {
        error = string.Empty;
        var serverUrl = ServerUrlBox.Text.Trim();
        var apiKey = ApiKeyBox.Password.Trim();

        if (string.IsNullOrEmpty(serverUrl))
        {
            _authService.ServerUrl = null;
            _authService.ApiKey = null;
            _authService.IsSyncEnabled = true;
            return true;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            error = "在线模式需要填写 API Key";
            return false;
        }

        _authService.ServerUrl = serverUrl;
        _authService.ApiKey = apiKey;

        if (SyncEnabledSwitch.IsChecked == true)
        {
            _authService.IsSyncEnabled = true;
        }
        else if (SyncEnabledSwitch.Visibility == Visibility.Visible)
        {
            _authService.IsSyncEnabled = false;
        }

        return true;
    }

    private bool TryApplyCredentialsFromFields(out string error)
    {
        error = string.Empty;
        var serverUrl = ServerUrlBox.Text.Trim();
        var apiKey = ApiKeyBox.Password.Trim();

        if (string.IsNullOrEmpty(serverUrl))
        {
            error = "请填入服务器地址";
            return false;
        }

        if (string.IsNullOrEmpty(apiKey))
        {
            error = "请填入 API Key";
            return false;
        }

        _authService.ServerUrl = serverUrl;
        _authService.ApiKey = apiKey;
        return true;
    }

    private bool TryBuildSyncSettings(out SyncSettings settings, out string error)
    {
        settings = SyncSettings.Load();
        error = string.Empty;

        if (!TryParsePositiveInt(PeriodicPushBox.Text, out var periodicPush))
        {
            error = "笔记 Push 周期请输入 5–600 之间的整数（秒）";
            return false;
        }

        if (!TryParsePositiveInt(ImageUploadBox.Text, out var imageUpload))
        {
            error = "图片上传周期请输入 5–600 之间的整数（秒）";
            return false;
        }

        if (!TryParsePositiveInt(ImagePullBox.Text, out var imagePull))
        {
            error = "图片拉取周期请输入 10–3600 之间的整数（秒）";
            return false;
        }

        settings.PeriodicPushIntervalSeconds = periodicPush;
        settings.FlushOnExit = FlushOnExitBox.IsChecked == true;
        settings.SyncOnAppActivated = SyncOnActivatedBox.IsChecked == true;
        settings.SyncOnNetworkRecover = SyncOnNetworkRecoverBox.IsChecked == true;
        settings.ImageUploadIntervalSeconds = imageUpload;
        settings.ImagePullIntervalSeconds = imagePull;
        settings.Normalize();
        return true;
    }

    private bool TryBuildDataSafetySettings(out DataSafetySettings settings, out string error)
    {
        settings = new DataSafetySettings();
        error = string.Empty;

        if (ProfileCombo.SelectedItem is not ProfileOption profileOption)
        {
            error = "请选择安全档位";
            return false;
        }

        if (!int.TryParse(BackupRetentionBox.Text.Trim(), out var retention) || retention < 3 || retention > 90)
        {
            error = "保留备份份数请输入 3–90 之间的整数";
            return false;
        }

        if (!int.TryParse(BackupIntervalBox.Text.Trim(), out var intervalHours) || intervalHours < 0 || intervalHours > 168)
        {
            error = "定时备份间隔请输入 0–168 之间的整数（小时）";
            return false;
        }

        if (!int.TryParse(EditJournalRetentionBox.Text.Trim(), out var journalDays) || journalDays < 7 || journalDays > 365)
        {
            error = "编辑日志保留请输入 7–365 之间的整数（天）";
            return false;
        }

        if (!int.TryParse(TrashRetentionBox.Text.Trim(), out var trashDays) || trashDays < 7 || trashDays > 365)
        {
            error = "回收站保留请输入 7–365 之间的整数（天）";
            return false;
        }

        if (!int.TryParse(RemindExportBox.Text.Trim(), out var remindDays) || remindDays < 1 || remindDays > 90)
        {
            error = "导出提醒间隔请输入 1–90 之间的整数（天）";
            return false;
        }

        settings.Profile = profileOption.Profile;
        settings.EnableAutoBackup = EnableAutoBackupBox.IsChecked == true;
        settings.BackupOnExit = BackupOnExitBox.IsChecked == true;
        settings.BackupRetentionCount = retention;
        settings.BackupIntervalHours = intervalHours;
        settings.BackupDirectoryUseDefault = BackupUseDefaultBox.IsChecked == true;
        settings.BackupDirectory = BackupCustomPathBox.Text.Trim();

        if (!settings.BackupDirectoryUseDefault)
        {
            if (string.IsNullOrWhiteSpace(settings.BackupDirectory))
            {
                error = "请选择自定义备份文件夹";
                return false;
            }

            if (!LocalDatabasePaths.TryEnsureBackupDirectory(settings.BackupDirectory, out var dirError))
            {
                error = dirError;
                return false;
            }
        }

        settings.EnableEditJournal = EnableEditJournalBox.IsChecked == true;
        settings.EditJournalRetentionDays = journalDays;
        settings.TrashRetentionDays = trashDays;
        settings.EnableExportReminder = EnableExportReminderBox.IsChecked == true;
        settings.RemindExportAfterDays = remindDays;
        settings.Normalize();
        return true;
    }

    private static bool TryParsePositiveInt(string text, out int value)
    {
        value = 0;
        return int.TryParse(text.Trim(), out value) && value > 0;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// 未点「确定」却已切换同步开关：凭据已写入，需立即重启引擎才能生效
    /// </summary>
    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DialogResult == true || !_syncEnabledToggled || _onDismissWithoutSaveAfterToggle == null)
        {
            return;
        }

        try
        {
            await _onDismissWithoutSaveAfterToggle();
        }
        catch (Exception ex)
        {
            LogHelper.Error("关闭同步设置后重启引擎失败", ex);
        }
    }

    private void OnBackupLocationModeChanged(object sender, RoutedEventArgs e)
    {
        UpdateBackupLocationUi();
        UpdateBackupPathHint();
    }

    private void UpdateBackupLocationUi()
    {
        var useDefault = BackupUseDefaultBox.IsChecked == true;
        BackupCustomPathBox.IsEnabled = !useDefault;
        BackupBrowseButton.IsEnabled = !useDefault;
        BackupDefaultPathText.Visibility = useDefault ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBackupPathHint()
    {
        var preview = new DataSafetySettings
        {
            BackupDirectoryUseDefault = BackupUseDefaultBox.IsChecked == true,
            BackupDirectory = BackupCustomPathBox.Text.Trim(),
        };
        preview.Normalize();
        var effective = LocalDatabasePaths.ResolveBackupDirectory(preview);
        BackupPathHint.Text = $"当前生效目录：{effective}";

        if (!preview.BackupDirectoryUseDefault
            && LocalDatabasePaths.IsBackupDirectoryInsideApp(effective))
        {
            BackupPathHint.Text += "\n提示：备份目录位于应用目录内，存在误删风险，建议使用独立文件夹。";
        }
    }

    private void OnBrowseBackupFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 local.db 备份目录",
        };

        if (!string.IsNullOrWhiteSpace(BackupCustomPathBox.Text))
        {
            dialog.InitialDirectory = BackupCustomPathBox.Text.Trim();
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        BackupUseDefaultBox.IsChecked = false;
        BackupCustomPathBox.Text = dialog.FolderName;
        UpdateBackupLocationUi();
        UpdateBackupPathHint();
    }

    private void OnManageBackups(object sender, RoutedEventArgs e)
    {
        if (!TryBuildDataSafetySettings(out var settings, out var error))
        {
            ShowStatus(error, ThemeBrushKeys.Danger);
            return;
        }

        settings.LastBackupUtc = _dataSafety.LastBackupUtc;
        var dialog = new BackupManagerWindow(settings)
        {
            Owner = this,
        };
        dialog.ShowDialog();
        _dataSafety.LastBackupUtc = DataSafetySettings.Load().LastBackupUtc;
        UpdateBackupPathHint();
    }

    /// <summary>用主题画刷显示底部状态（Success / Danger / TextMuted 等）</summary>
    private void ShowStatus(string text, string brushKey)
    {
        StatusText.Text = text;
        StatusText.Foreground = TryFindResource(brushKey) as Brush
            ?? TryFindResource(ThemeBrushKeys.TextMuted) as Brush
            ?? Brushes.Gray;
    }

    private sealed class ProfileOption(DataSafetyProfile profile, string label)
    {
        public DataSafetyProfile Profile { get; } = profile;
        public string Label { get; } = label;
    }
}
