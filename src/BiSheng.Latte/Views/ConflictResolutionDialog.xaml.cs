using System.Windows;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Diff;
using BiSheng.Shared;

namespace BiSheng.Latte.Views;

/// <summary>
/// 同步冲突解决：对照 / 行级 Diff / 可读预览，支持保留本地、远端、手动合并与批量操作
/// </summary>
public partial class ConflictResolutionDialog : Window
{
    private readonly SyncService _syncService;
    private List<SyncConflict> _conflicts;
    private int _currentIndex;

    public ConflictResolutionDialog(SyncService syncService)
    {
        InitializeComponent();
        _syncService = syncService;
        _conflicts = _syncService.GetUnresolvedConflicts();
        _currentIndex = 0;
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (_conflicts.Count == 0)
        {
            ConflictCountText.Text = "没有未解决的冲突";
            ActionPairText.Text = "";
            LocalContentBox.Text = "";
            RemoteContentBox.Text = "";
            LocalTimestamp.Text = "";
            RemoteTimestamp.Text = "";
            DiffLinesList.ItemsSource = null;
            LocalPreview.SetContent("");
            RemotePreview.SetContent("");
            KeepLocalButton.Content = "保留本地版本";
            KeepRemoteButton.Content = "保留远端版本";
            Title = "同步冲突解决（已全部解决）";
            ApplyViewMode();
            return;
        }

        var conflict = _conflicts[_currentIndex];
        Title = $"同步冲突解决（{_currentIndex + 1} / {_conflicts.Count}）";
        ConflictCountText.Text =
            $"共 {_conflicts.Count} 个冲突，当前第 {_currentIndex + 1} 个：{conflict.EntityTitle}"
            + (conflict.EntityType == EntityTypes.Folder ? "（文件夹）" : "");
        ActionPairText.Text = ConflictDialogCopy.FormatActionPair(conflict.LocalAction, conflict.RemoteAction);

        LocalContentBox.Text = conflict.LocalContent;
        RemoteContentBox.Text = conflict.RemoteContent;
        LocalTimestamp.Text =
            $"{ConflictDialogCopy.FormatAction(conflict.LocalAction)} · {conflict.LocalUpdatedAt:yyyy-MM-dd HH:mm:ss}";
        RemoteTimestamp.Text =
            $"{ConflictDialogCopy.FormatAction(conflict.RemoteAction)} · {conflict.RemoteUpdatedAt:yyyy-MM-dd HH:mm:ss}";

        KeepLocalButton.Content = ConflictDialogCopy.KeepLocalButton(conflict.LocalAction);
        KeepRemoteButton.Content = ConflictDialogCopy.KeepRemoteButton(conflict.LocalAction, conflict.RemoteAction);

        DiffLinesList.ItemsSource = ConflictTextDiffer.BuildUnified(conflict.LocalContent, conflict.RemoteContent);
        LocalPreview.SetContent(conflict.LocalContent);
        RemotePreview.SetContent(conflict.RemoteContent);

        PrevButton.IsEnabled = _currentIndex > 0;
        NextButton.IsEnabled = _currentIndex < _conflicts.Count - 1;
        ApplyViewMode();
    }

    private void OnViewModeChanged(object sender, RoutedEventArgs e) => ApplyViewMode();

    private void ApplyViewMode()
    {
        var sideBySide = ViewSideBySide.IsChecked == true;
        var diff = ViewDiff.IsChecked == true;
        SideBySidePanel.Visibility = sideBySide ? Visibility.Visible : Visibility.Collapsed;
        DiffPanel.Visibility = diff ? Visibility.Visible : Visibility.Collapsed;
        PreviewPanel.Visibility = ViewPreview.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPrev(object sender, RoutedEventArgs e)
    {
        if (_currentIndex > 0)
        {
            _currentIndex--;
            UpdateUI();
        }
    }

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_currentIndex < _conflicts.Count - 1)
        {
            _currentIndex++;
            UpdateUI();
        }
    }

    private void OnKeepLocal(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0)
        {
            return;
        }

        _syncService.ResolveKeepLocal(_conflicts[_currentIndex].Id);
        RemoveCurrentAndAdvance();
    }

    private void OnKeepRemote(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0)
        {
            return;
        }

        _syncService.ResolveKeepRemote(_conflicts[_currentIndex].Id);
        RemoveCurrentAndAdvance();
    }

    private void OnMerge(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0)
        {
            return;
        }

        var conflict = _conflicts[_currentIndex];
        var mergeWin = new MergeEditWindow(conflict.LocalContent, conflict.RemoteContent)
        {
            Owner = this,
            Title = $"手动合并：{conflict.EntityTitle}"
        };

        if (mergeWin.ShowDialog() == true)
        {
            _syncService.ResolveMerged(conflict.Id, mergeWin.MergedContent);
            RemoveCurrentAndAdvance();
        }
    }

    private void OnKeepAllLocal(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0)
        {
            return;
        }

        if (!AppDialog.Confirm(
                $"确定将所有 {_conflicts.Count} 个冲突都保留本地版本？\n远端版本将被丢弃。",
                "确认批量操作"))
        {
            return;
        }

        foreach (var conflict in _conflicts.ToList())
        {
            _syncService.ResolveKeepLocal(conflict.Id);
        }

        FinishAllResolved();
    }

    private void OnKeepAllRemote(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0)
        {
            return;
        }

        if (!AppDialog.Confirm(
                $"确定将所有 {_conflicts.Count} 个冲突都保留远端版本？\n本地未推送的对应变更将被丢弃。",
                "确认批量操作"))
        {
            return;
        }

        foreach (var conflict in _conflicts.ToList())
        {
            _syncService.ResolveKeepRemote(conflict.Id);
        }

        FinishAllResolved();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void FinishAllResolved()
    {
        _conflicts.Clear();
        _currentIndex = 0;
        UpdateUI();
        AppDialog.Success("所有冲突已解决！", "完成");
        DialogResult = true;
        Close();
    }

    private void RemoveCurrentAndAdvance()
    {
        _conflicts.RemoveAt(_currentIndex);

        if (_conflicts.Count == 0)
        {
            _currentIndex = 0;
        }
        else if (_currentIndex >= _conflicts.Count)
        {
            _currentIndex = _conflicts.Count - 1;
        }

        UpdateUI();

        if (_conflicts.Count == 0)
        {
            AppDialog.Success("所有冲突已解决！", "完成");
            DialogResult = true;
            Close();
        }
    }
}
