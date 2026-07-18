namespace BiSheng.Latte.Services;

/// <summary>
/// 主窗口宿主接口：ViewModel 命令需要 Window / 对话框时通过此接口回调 View 层
/// </summary>
public interface IMainWindowHost
{
    void Minimize();

    void ToggleMaximize();

    void Close();

    void ApplyAppearanceSettings();

    /// <summary>打开同步与安全设置对话框，返回 true 表示用户已保存</summary>
    bool? ShowSyncSettingsDialog();

    /// <summary>打开外观设置对话框，返回 true 表示用户已确认</summary>
    bool? ShowAppearanceDialog();

    /// <summary>打开冲突解决对话框，返回 true 表示用户已处理冲突</summary>
    bool? ShowConflictDialog();

    /// <summary>打开本地备份管理对话框</summary>
    void ShowBackupManagerDialog();
}
