using System.Windows;
using BiSheng.Latte.Models;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Views;

/// <summary>本地数据库备份管理弹窗</summary>
public partial class BackupManagerWindow : Window
{
    private readonly BackupManagerViewModel _viewModel;

    /// <summary>使用指定数据安全设置打开（null 则从磁盘加载）</summary>
    public BackupManagerWindow(DataSafetySettings? settings = null)
    {
        _viewModel = new BackupManagerViewModel(settings ?? DataSafetySettings.Load());
        DataContext = _viewModel;
        InitializeComponent();
        Loaded += (_, _) => _viewModel.RefreshCommand.Execute(null);
    }
}
