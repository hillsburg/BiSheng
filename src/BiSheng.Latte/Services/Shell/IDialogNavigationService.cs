using BiSheng.Latte.Models;

namespace BiSheng.Latte.Services.Shell;

/// <summary>主窗口对话框与导出导航服务</summary>
public interface IDialogNavigationService
{
    /// <summary>绑定壳层回调（MainViewModel 构造末尾调用一次）</summary>
    void BindShell(
        Func<IMainWindowHost?> getHost,
        Func<string?> getEditorText,
        Action<SyncSettings> setSyncSettings,
        Func<Task> restartSyncEngine,
        Action updateConnectionStatus,
        Action refreshConflictsAfterDialog,
        Action<EditorNavigationIntent> navigateFromSearch,
        Func<bool> isConnected);

    /// <summary>打开同步与安全设置</summary>
    Task OpenSyncSettingsAsync();

    /// <summary>打开回收站</summary>
    void OpenTrash();

    /// <summary>打开本地备份管理</summary>
    void OpenBackupManager();

    /// <summary>打开全文搜索</summary>
    void OpenNoteSearch();

    /// <summary>打开外观设置</summary>
    void OpenAppearance();

    /// <summary>打开当前笔记历史版本</summary>
    void OpenNoteHistory();

    /// <summary>打开冲突解决对话框</summary>
    void OpenConflictDialog();

    /// <summary>导出全库为 BiSheng Archive</summary>
    Task ExportFullLibraryAsync();
}
