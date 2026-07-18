using BiSheng.Latte.Models;
using BiSheng.Latte.Services.Search;
using BiSheng.Latte.ViewModels;
using System.Windows;

namespace BiSheng.Latte.Services.Shell;

/// <summary>主窗口对话框与导出导航：从 MainViewModel 抽离的壳层打开逻辑</summary>
public sealed class DialogNavigationService : IDialogNavigationService
{
    private readonly AuthService _authService;
    private readonly AppUpdateService _updates;
    private readonly SyncService _syncEngine;
    private readonly ImageSyncService _imageSync;
    private readonly EditorViewModel _editor;
    private readonly NoteRevisionService _noteRevisions;
    private readonly ExportService _export;
    private readonly TrashService _trash;
    private readonly INoteContentSearchService _noteContentSearch;
    private readonly NavigationViewModel _navigation;

    private Func<IMainWindowHost?>? _getHost;
    private Func<string?>? _getEditorText;
    private Action<SyncSettings>? _setSyncSettings;
    private Func<Task>? _restartSyncEngine;
    private Action? _updateConnectionStatus;
    private Action? _refreshConflictsAfterDialog;
    private Action<EditorNavigationIntent>? _navigateFromSearch;
    private Func<bool>? _isConnected;

    /// <summary>构造对话框导航服务</summary>
    public DialogNavigationService(
        AuthService authService,
        AppUpdateService updates,
        SyncService syncEngine,
        ImageSyncService imageSync,
        EditorViewModel editor,
        NoteRevisionService noteRevisions,
        ExportService export,
        TrashService trash,
        INoteContentSearchService noteContentSearch,
        NavigationViewModel navigation)
    {
        _authService = authService;
        _updates = updates;
        _syncEngine = syncEngine;
        _imageSync = imageSync;
        _editor = editor;
        _noteRevisions = noteRevisions;
        _export = export;
        _trash = trash;
        _noteContentSearch = noteContentSearch;
        _navigation = navigation;
    }

    /// <inheritdoc />
    public void BindShell(
        Func<IMainWindowHost?> getHost,
        Func<string?> getEditorText,
        Action<SyncSettings> setSyncSettings,
        Func<Task> restartSyncEngine,
        Action updateConnectionStatus,
        Action refreshConflictsAfterDialog,
        Action<EditorNavigationIntent> navigateFromSearch,
        Func<bool> isConnected)
    {
        _getHost = getHost;
        _getEditorText = getEditorText;
        _setSyncSettings = setSyncSettings;
        _restartSyncEngine = restartSyncEngine;
        _updateConnectionStatus = updateConnectionStatus;
        _refreshConflictsAfterDialog = refreshConflictsAfterDialog;
        _navigateFromSearch = navigateFromSearch;
        _isConnected = isConnected;
    }

    /// <inheritdoc />
    public async Task OpenSyncSettingsAsync()
    {
        if (_getHost?.Invoke()?.ShowSyncSettingsDialog() != true)
        {
            return;
        }

        var settings = SyncSettings.Load();
        _setSyncSettings?.Invoke(settings);
        _syncEngine.ApplySettings(settings);
        _imageSync.ApplySettings(settings);
        _editor.RefreshRevisionIdleTimer();
        _updateConnectionStatus?.Invoke();

        // 确认关闭时再重启引擎；开关过程中的即时重启已去掉，避免双重 Stop/Start
        if (_restartSyncEngine != null)
        {
            await _restartSyncEngine();
        }
    }

    /// <inheritdoc />
    public void OpenTrash()
    {
        var dialog = new Views.TrashWindow(_trash)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    /// <inheritdoc />
    public void OpenBackupManager()
    {
        _getHost?.Invoke()?.ShowBackupManagerDialog();
    }

    /// <inheritdoc />
    public void OpenNoteSearch()
    {
        if (_navigateFromSearch == null)
        {
            return;
        }

        var dialog = new Views.NoteSearchWindow(
            _noteContentSearch,
            _navigateFromSearch,
            _navigation.SearchInput)
        {
            Owner = Application.Current.MainWindow
        };

        dialog.ShowDialog();
        Application.Current.MainWindow?.Activate();
    }

    /// <inheritdoc />
    public void OpenAppearance()
    {
        var host = _getHost?.Invoke();
        if (host?.ShowAppearanceDialog() == true)
        {
            host.ApplyAppearanceSettings();
        }
    }

    /// <inheritdoc />
    public void OpenAbout()
    {
        _getHost?.Invoke()?.ShowAboutDialog();
    }

    /// <inheritdoc />
    public void OpenNoteHistory()
    {
        var note = _editor.CurrentNote;
        if (note == null)
        {
            AppDialog.Info("请先打开一篇笔记。", "历史版本");
            return;
        }

        _editor.ForceSave(_getEditorText?.Invoke());

        var useServer = _isConnected?.Invoke() == true && !_authService.IsOfflineMode;
        var dialog = new Views.NoteHistoryWindow(_noteRevisions, _editor, note, useServer)
        {
            Owner = Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    /// <inheritdoc />
    public void OpenConflictDialog()
    {
        if (_getHost?.Invoke()?.ShowConflictDialog() != true)
        {
            return;
        }

        _refreshConflictsAfterDialog?.Invoke();
    }

    /// <inheritdoc />
    public async Task ExportFullLibraryAsync()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择 BiSheng Archive 导出目录" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var path = await _export.ExportFullLibraryAsync(dialog.FolderName);
            AppDialog.Success(
                $"全库已导出到：\n{path}\n\n包含 manifest.json、notes/ 与 images/。",
                "导出成功");
        }
        catch (Exception ex)
        {
            AppDialog.Error(
                $"导出失败：{ex.Message}",
                "导出失败");
        }
    }
}
