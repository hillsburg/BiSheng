using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiSheng.Latte.ViewModels;

/// <summary>备份管理弹窗 ViewModel</summary>
public partial class BackupManagerViewModel : ObservableObject
{
    private readonly DataSafetySettings _settings;
    private string _backupDirectory = string.Empty;

    public BackupManagerViewModel(DataSafetySettings settings)
    {
        _settings = settings.Clone();
    }

    /// <summary>备份列表</summary>
    public ObservableCollection<LocalDatabaseBackupRepository.BackupListItem> Backups { get; } = [];

    [ObservableProperty]
    private LocalDatabaseBackupRepository.BackupListItem? _selectedBackup;

    [ObservableProperty]
    private string _headerDirectory = string.Empty;

    [ObservableProperty]
    private string _headerPolicy = string.Empty;

    [ObservableProperty]
    private string _detailText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _showLegacyHint;

    [ObservableProperty]
    private string _legacyHintText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canDelete;

    /// <summary>当前是否有备份条目（驱动空状态）</summary>
    [ObservableProperty]
    private bool _hasBackups;

    /// <summary>加载备份列表</summary>
    [RelayCommand]
    public void Refresh()
    {
        _backupDirectory = LocalDatabasePaths.ResolveBackupDirectory(_settings);
        var items = LocalDatabaseBackupRepository.ListBackups(_backupDirectory);

        Backups.Clear();
        foreach (var item in items)
        {
            Backups.Add(item);
        }

        var lastBackup = _settings.LastBackupUtc.HasValue
            ? _settings.LastBackupUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "尚无记录";
        HeaderDirectory = _backupDirectory;
        HeaderPolicy = $"保留 {_settings.BackupRetentionCount} 份 · 上次成功备份 {lastBackup}";

        var legacyCount = items.Count(item => item.IsLegacy);
        ShowLegacyHint = legacyCount > 0;
        LegacyHintText = legacyCount > 0
            ? $"目录中有 {legacyCount} 个无元数据的旧备份，仅显示文件大小与时间；新创建的备份会包含完整详情。"
            : string.Empty;

        HasBackups = Backups.Count > 0;
        CanDelete = HasBackups;
        StatusText = HasBackups
            ? $"共 {Backups.Count} 个备份文件。"
            : "当前目录尚无备份。";

        if (SelectedBackup == null && Backups.Count > 0)
        {
            SelectedBackup = Backups[0];
        }
        else
        {
            UpdateDetail();
        }
    }

    partial void OnSelectedBackupChanged(LocalDatabaseBackupRepository.BackupListItem? value)
    {
        RevealSelectedCommand.NotifyCanExecuteChanged();
        UpdateDetail();
    }

    /// <summary>立即创建备份</summary>
    [RelayCommand(CanExecute = nameof(CanRunOperation))]
    private void BackupNow()
    {
        IsBusy = true;
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (!LocalDatabaseBackupService.TryCreateBackup(_settings, BackupTrigger.Manual, out var path))
            {
                StatusText = "备份失败，请检查目录权限或查看日志。";
                AppDialog.Warning("备份失败，请查看日志或检查备份目录权限。", "备份失败");
                return;
            }

            var persisted = DataSafetySettings.Load();
            persisted.LastBackupUtc = DateTime.UtcNow;
            persisted.Save();
            _settings.LastBackupUtc = persisted.LastBackupUtc;

            Refresh();
            StatusText = $"备份已创建：{Path.GetFileName(path)}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>删除所选备份</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteSelected))]
    private void DeleteSelected()
    {
        if (SelectedBackup == null)
        {
            return;
        }

        if (!AppDialog.ConfirmDanger(
                $"确定删除备份「{SelectedBackup.FileName}」？\n此操作无法撤销。",
                "删除备份"))
        {
            return;
        }

        if (!LocalDatabaseBackupRepository.TryDeleteBackup(SelectedBackup.FilePath, out var error))
        {
            StatusText = $"删除失败：{error}";
            AppDialog.Error(error, "删除失败");
            return;
        }

        SelectedBackup = null;
        Refresh();
        StatusText = "备份已删除。";
    }

    /// <summary>打开备份目录</summary>
    [RelayCommand]
    private void OpenFolder()
    {
        LocalDatabaseBackupRepository.OpenBackupDirectory(
            LocalDatabasePaths.ResolveBackupDirectory(_settings));
    }

    /// <summary>在资源管理器中定位所选备份</summary>
    [RelayCommand(CanExecute = nameof(CanRevealSelected))]
    private void RevealSelected()
    {
        if (SelectedBackup != null)
        {
            LocalDatabaseBackupRepository.RevealInExplorer(SelectedBackup.FilePath);
        }
    }

    private bool CanRunOperation() => !IsBusy;

    private bool CanDeleteSelected() => SelectedBackup != null && !IsBusy;

    private bool CanRevealSelected() => SelectedBackup != null && !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        BackupNowCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        RevealSelectedCommand.NotifyCanExecuteChanged();
    }

    private void UpdateDetail()
    {
        if (SelectedBackup == null)
        {
            DetailText = Backups.Count == 0
                ? "尚无备份。点击「立即备份」创建第一份 local.db 快照。\n\n说明：备份仅包含 local.db；图片在 %LocalAppData%\\BiSheng\\Latte\\images\\ 中。"
                : "请从列表中选择一个备份。";
            return;
        }

        DetailText = SelectedBackup.BuildDetailText();
    }
}
