using System.Windows;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Views;

/// <summary>local.db 完整性检查失败时的引导对话框</summary>
public partial class DatabaseIntegrityDialog : Window
{
    /// <summary>用户是否选择打开备份管理</summary>
    public bool OpenBackupManagerRequested { get; private set; }

    /// <summary>构造完整性警告对话框</summary>
    public DatabaseIntegrityDialog(string detailMessage)
    {
        InitializeComponent();
        DetailText.Text = string.IsNullOrWhiteSpace(detailMessage)
            ? "数据库文件可能已损坏，建议从最近的本地备份恢复。"
            : detailMessage;
    }

    private void OnOpenBackupManager(object sender, RoutedEventArgs e)
    {
        OpenBackupManagerRequested = true;
        DialogResult = true;
        Close();
    }

    private void OnDismiss(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
