using System.Windows;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using BiSheng.Shared;

namespace BiSheng.Latte.Views;

public partial class TrashWindow : Window
{
    private readonly TrashService _trash;

    /// <summary>构造回收站窗口（导航增量由 TrashService 发布）</summary>
    public TrashWindow(TrashService trash)
    {
        _trash = trash;
        InitializeComponent();
        Reload();
    }

    private void Reload()
    {
        var retention = DataSafetySettings.Load().TrashRetentionDays;
        HintText.Text = $"已删除的笔记与文件夹保留 {retention} 天，到期后将从本机永久移除（云端仍为软删除状态）。";

        var rows = _trash.GetTrashItems().Select(item => new TrashRow(item)).ToList();
        TrashList.ItemsSource = rows;
        EmptyStateText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RestoreButton.IsEnabled = rows.Count > 0;
        DeleteButton.IsEnabled = rows.Count > 0;
        EmptyButton.IsEnabled = rows.Count > 0;
    }

    private TrashRow? SelectedRow =>
        TrashList.SelectedItem as TrashRow;

    private void OnRestore(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow;
        if (row == null)
        {
            AppDialog.Info("请先选择要恢复的项目。", "回收站");
            return;
        }

        _trash.Restore(row.EntityType, row.EntityId);
        Reload();
    }

    private void OnDeletePermanently(object sender, RoutedEventArgs e)
    {
        var row = SelectedRow;
        if (row == null)
        {
            AppDialog.Info("请先选择要永久删除的项目。", "回收站");
            return;
        }

        if (!AppDialog.ConfirmDanger(
                $"确定永久删除「{row.DisplayName}」？此操作无法撤销。",
                "永久删除"))
        {
            return;
        }

        _trash.PurgePermanently(row.EntityType, row.EntityId);
        Reload();
    }

    private void OnEmptyTrash(object sender, RoutedEventArgs e)
    {
        if (!AppDialog.ConfirmDanger(
                "确定清空回收站？所有项目将从本机永久删除。",
                "清空回收站"))
        {
            return;
        }

        _trash.EmptyTrash();
        Reload();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private sealed class TrashRow
    {
        public TrashRow(TrashService.TrashItem item)
        {
            EntityType = item.EntityType;
            EntityId = item.EntityId;
            DisplayName = item.DisplayName;
            DeletedAtLocal = item.DeletedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            DaysRemaining = item.DaysRemaining;
            TypeLabel = item.EntityType == EntityTypes.Folder ? "文件夹" : "笔记";
        }

        public string EntityType { get; }
        public Guid EntityId { get; }
        public string DisplayName { get; }
        public string DeletedAtLocal { get; }
        public int DaysRemaining { get; }
        public string TypeLabel { get; }
    }
}
