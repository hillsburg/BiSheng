using System.Windows;
using BiSheng.Latte.Models;
using BiSheng.Latte.Views;

namespace BiSheng.Latte.Services;

/// <summary>备份管理弹窗与完整性引导的统一入口</summary>
public static class BackupManagerHost
{
    /// <summary>打开备份管理对话框</summary>
    public static void ShowManager(Window owner, DataSafetySettings? settings = null)
    {
        var dialog = new BackupManagerWindow(settings ?? DataSafetySettings.Load())
        {
            Owner = owner,
        };
        dialog.ShowDialog();
    }

    /// <summary>完整性检查失败时提示，用户确认则打开备份管理</summary>
    public static void PromptIntegrityFailure(Window owner, string detailMessage)
    {
        var dialog = new DatabaseIntegrityDialog(detailMessage)
        {
            Owner = owner,
        };

        if (dialog.ShowDialog() == true && dialog.OpenBackupManagerRequested)
        {
            ShowManager(owner);
        }
    }
}
