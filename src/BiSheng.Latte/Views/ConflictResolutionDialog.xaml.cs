using System.Windows;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Views;

/// <summary>
/// 同步冲突解决对话框
/// 
/// 功能：
/// - 展示所有未解决的同步冲突（本地 vs 远端内容对比）
/// - 提供三种解决方式：保留本地、保留远端、手动合并
/// - 支持冲突之间的导航（上一个/下一个）
/// - "全部保留本地" 批量解决所有冲突
/// 
/// 手动合并流程：
/// 1. 点击"手动合并"按钮 → 弹出可编辑对话框
/// 2. 左右两个文本框变为可编辑，用户可自行修改内容
/// 3. 点击"确认合并" → 将编辑后的内容作为合并结果保存
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

    // ========================================================
    //  UI 更新
    // ========================================================

    /// <summary>
    /// 根据当前冲突索引更新界面显示
    /// </summary>
    private void UpdateUI()
    {
        if (_conflicts.Count == 0)
        {
            ConflictCountText.Text = "没有未解决的冲突";
            LocalContentBox.Text = "";
            RemoteContentBox.Text = "";
            LocalTimestamp.Text = "";
            RemoteTimestamp.Text = "";
            Title = "同步冲突解决（已全部解决）";
            return;
        }

        var conflict = _conflicts[_currentIndex];

        Title = $"同步冲突解决（{_currentIndex + 1} / {_conflicts.Count}）";
        ConflictCountText.Text = $"共 {_conflicts.Count} 个冲突，当前第 {_currentIndex + 1} 个：{conflict.EntityTitle}";

        LocalContentBox.Text = conflict.LocalContent;
        RemoteContentBox.Text = conflict.RemoteContent;
        LocalTimestamp.Text = $"修改时间：{conflict.LocalUpdatedAt:yyyy-MM-dd HH:mm:ss}";
        RemoteTimestamp.Text = $"修改时间：{conflict.RemoteUpdatedAt:yyyy-MM-dd HH:mm:ss}";

        // 更新导航按钮状态
        PrevButton.IsEnabled = _currentIndex > 0;
        NextButton.IsEnabled = _currentIndex < _conflicts.Count - 1;
    }

    // ========================================================
    //  导航事件
    // ========================================================

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

    // ========================================================
    //  解决冲突
    // ========================================================

    /// <summary>
    /// 保留本地版本：本地内容不变，仅标记冲突为已解决
    /// </summary>
    private void OnKeepLocal(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0) return;

        var conflict = _conflicts[_currentIndex];
        _syncService.ResolveKeepLocal(conflict.Id);

        RemoveCurrentAndAdvance();
    }

    /// <summary>
    /// 保留远端版本：用远端内容覆盖本地，标记冲突为已解决
    /// </summary>
    private void OnKeepRemote(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0) return;

        var conflict = _conflicts[_currentIndex];
        _syncService.ResolveKeepRemote(conflict.Id);

        RemoveCurrentAndAdvance();
    }

    /// <summary>
    /// 手动合并：弹出编辑对话框，用户编辑后确认合并结果
    /// </summary>
    private void OnMerge(object sender, RoutedEventArgs e)
    {
        if (_conflicts.Count == 0) return;

        var conflict = _conflicts[_currentIndex];

        // 打开合并编辑对话框：左右两个可编辑文本框
        var mergeWin = new MergeEditWindow(conflict.LocalContent, conflict.RemoteContent)
        {
            Owner = this,
            Title = $"手动合并：{conflict.EntityTitle}"
        };

        if (mergeWin.ShowDialog() == true)
        {
            // 用户确认了合并结果
            _syncService.ResolveMerged(conflict.Id, mergeWin.MergedContent);
            RemoveCurrentAndAdvance();
        }
    }

    /// <summary>
    /// 全部保留本地版本：批量解决所有冲突
    /// </summary>
    private void OnKeepAllLocal(object sender, RoutedEventArgs e)
    {
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

        _conflicts.Clear();
        _currentIndex = 0;
        UpdateUI();
    }

    /// <summary>
    /// 关闭对话框
    /// </summary>
    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    // ========================================================
    //  辅助方法
    // ========================================================

    /// <summary>
    /// 移除当前冲突，自动前进到下一个或显示完成状态
    /// </summary>
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

        // 如果所有冲突都已解决，自动关闭
        if (_conflicts.Count == 0)
        {
            AppDialog.Success("所有冲突已解决！", "完成");
            DialogResult = true;
            Close();
        }
    }
}

/// <summary>
/// 手动合并编辑窗口：左右两个可编辑文本框 + 确认按钮
/// 用户可以自由编辑两侧内容，最终确认合并结果
/// </summary>
public partial class MergeEditWindow : Window
{
    /// <summary>用户确认后的合并结果</summary>
    public string MergedContent { get; private set; } = string.Empty;

    public MergeEditWindow(string localContent, string remoteContent)
    {
        // 使用代码创建 UI（避免额外 XAML 文件）
        Title = "手动合并";
        Width = 900;
        Height = 600;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xFA, 0xFA, 0xFA));

        var dock = new System.Windows.Controls.DockPanel();

        // 顶部说明
        var header = new System.Windows.Controls.Border
        {
            Background = System.Windows.Media.Brushes.White,
            Padding = new Thickness(20, 12, 20, 12),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        header.Child = new System.Windows.Controls.TextBlock
        {
            Text = "左右两栏都可编辑。编辑完成后点击【确认合并】，编辑后的内容将作为最终版本保存。",
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 12
        };
        System.Windows.Controls.DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        dock.Children.Add(header);

        // 底部按钮
        var footer = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xF5, 0xF5, 0xF5)),
            Padding = new Thickness(20, 10, 20, 10),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(0, 1, 0, 0)
        };

        var btnPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var confirmBtn = new System.Windows.Controls.Button
        {
            Content = "确认合并",
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x8E, 0x44, 0xAD)),
            Foreground = System.Windows.Media.Brushes.White,
            Padding = new Thickness(24, 10, 24, 10),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(0, 0, 8, 0)
        };

        var leftBox = new System.Windows.Controls.TextBox();
        var rightBox = new System.Windows.Controls.TextBox();

        confirmBtn.Click += (_, _) =>
        {
            // 合并结果 = 左侧编辑后的内容
            MergedContent = leftBox.Text;
            DialogResult = true;
            Close();
        };
        btnPanel.Children.Add(confirmBtn);

        var cancelBtn = new System.Windows.Controls.Button
        {
            Content = "取消",
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x95, 0xA5, 0xA6)),
            Foreground = System.Windows.Media.Brushes.White,
            Padding = new Thickness(20, 10, 20, 10),
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        cancelBtn.Click += (_, _) => { DialogResult = false; Close(); };
        btnPanel.Children.Add(cancelBtn);

        footer.Child = btnPanel;
        System.Windows.Controls.DockPanel.SetDock(footer, System.Windows.Controls.Dock.Bottom);
        dock.Children.Add(footer);

        // 中间编辑区：左右两个可编辑文本框
        var grid = new System.Windows.Controls.Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // 左侧编辑框
        leftBox.Text = localContent;
        leftBox.AcceptsReturn = true;
        leftBox.AcceptsTab = true;
        leftBox.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        leftBox.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        leftBox.Padding = new Thickness(10);
        leftBox.FontFamily = new System.Windows.Media.FontFamily("Consolas, 'Courier New', monospace");
        leftBox.FontSize = 13;
        leftBox.TextWrapping = TextWrapping.NoWrap;

        var leftBorder = new System.Windows.Controls.Border
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        leftBorder.Child = leftBox;
        System.Windows.Controls.Grid.SetColumn(leftBorder, 0);
        grid.Children.Add(leftBorder);

        // 右侧编辑框
        rightBox.Text = remoteContent;
        rightBox.AcceptsReturn = true;
        rightBox.AcceptsTab = true;
        rightBox.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        rightBox.HorizontalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        rightBox.Padding = new Thickness(10);
        rightBox.FontFamily = new System.Windows.Media.FontFamily("Consolas, 'Courier New', monospace");
        rightBox.FontSize = 13;
        rightBox.TextWrapping = TextWrapping.NoWrap;

        var rightBorder = new System.Windows.Controls.Border
        {
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xE0)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        rightBorder.Child = rightBox;
        System.Windows.Controls.Grid.SetColumn(rightBorder, 2);
        grid.Children.Add(rightBorder);

        dock.Children.Add(grid);
        Content = dock;
    }
}
