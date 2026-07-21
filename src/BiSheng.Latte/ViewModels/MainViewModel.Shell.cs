using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace BiSheng.Latte.ViewModels;

/// <summary>MainViewModel 壳层：布局、连接状态、工具栏命令</summary>
public partial class MainViewModel
{
    /// <summary>主窗口宿主（由 MainWindow 在 Loaded 时注入）</summary>
    public IMainWindowHost? WindowHost { get; set; }

    /// <summary>导航面板应滚动并高亮当前活跃笔记</summary>
    public event Action? RevealActiveNoteRequested;

    /// <summary>笔记大纲</summary>
    public OutlineViewModel Outline { get; } = new();

    [ObservableProperty]
    private bool _isOutlinePanelVisible;

    [ObservableProperty]
    private double _folderColumnWidth = 220;

    [ObservableProperty]
    private double _noteColumnWidth = 280;

    [ObservableProperty]
    private double _outlineColumnWidth = 240;

    /// <summary>大纲列最小宽度：可见时 150，隐藏时 0（否则无法收起列）</summary>
    public const double OutlineColumnMinWidthWhenVisible = 150;

    /// <summary>绑定到大纲 ColumnDefinition.MinWidth</summary>
    public double OutlineColumnMinWidth =>
        IsOutlinePanelVisible ? OutlineColumnMinWidthWhenVisible : 0;

    /// <summary>归纳模式下合并树列宽</summary>
    [ObservableProperty]
    private double _mergedTreeColumnWidth = 280;

    /// <summary>导航区与编辑器之间的统一列宽（归纳=合并树宽；并列=文件夹+笔记+内部分割线）</summary>
    public double NavColumnWidth
    {
        get => IsTreeMode
            ? MergedTreeColumnWidth
            : FolderColumnWidth + NoteColumnWidth + SideBySideInternalSplitterWidth;
        set => ApplyNavColumnWidth(value);
    }

    private const double SideBySideInternalSplitterWidth = 3;

    private const double ToolbarSidebarWidth = 42;

    [ObservableProperty]
    private ToolbarPlacement _toolbarPlacement = ToolbarPlacement.Top;

    public bool IsToolbarTop => ToolbarPlacement == ToolbarPlacement.Top;

    public bool IsToolbarNavLeft => ToolbarPlacement == ToolbarPlacement.NavLeft;

    /// <summary>左侧工具栏列宽（顶部模式时为 0）</summary>
    public double ToolbarSidebarColumnWidth => IsToolbarNavLeft ? ToolbarSidebarWidth : 0;

    /// <summary>应用工具栏位置（外观设置预览 / 启动时调用）</summary>
    public void ApplyToolbarPlacement(ToolbarPlacement placement)
    {
        ToolbarPlacement = placement;
        OnPropertyChanged(nameof(IsToolbarTop));
        OnPropertyChanged(nameof(IsToolbarNavLeft));
        OnPropertyChanged(nameof(ToolbarSidebarColumnWidth));
    }

    [ObservableProperty]
    private int _conflictCount;

    [ObservableProperty]
    private bool _hasConflicts;

    /// <summary>工具栏连接 / 同步状态展示</summary>
    [ObservableProperty]
    private ConnectionDisplayInfo _connectionDisplay = ConnectionDisplayInfo.Default;

    /// <summary>同步引擎最近一次状态（供连接徽章解析）</summary>
    private SyncStatus _currentSyncStatus = SyncStatus.Offline;

    /// <summary>同步引擎最近活动文案（底栏优先展示）</summary>
    private string? _lastSyncActivityMessage;

    [ObservableProperty]
    private string _maximizeButtonGlyph = "\uE922";

    [ObservableProperty]
    private string _maximizeButtonTooltip = "最大化";

    /// <summary>折叠前缓存的列宽</summary>
    private double _storedFolderColumnWidth = 220;

    private double _storedNoteColumnWidth = 280;

    private double _storedOutlineColumnWidth = 240;

    private double _storedMergedTreeColumnWidth = 280;

    private bool _isApplyingLayout;

    private void NotifyNavColumnWidthChanged() => OnPropertyChanged(nameof(NavColumnWidth));

    private void ApplyNavColumnWidth(double value)
    {
        if (_isApplyingLayout)
            return;

        _isApplyingLayout = true;
        try
        {
            if (IsTreeMode)
            {
                MergedTreeColumnWidth = Clamp(value, 0, 500);
                if (MergedTreeColumnWidth > 0)
                    _storedMergedTreeColumnWidth = MergedTreeColumnWidth;
            }
            else
            {
                var available = Math.Max(0, value - SideBySideInternalSplitterWidth);
                var total = FolderColumnWidth + NoteColumnWidth;
                if (total > 0 && available > 0)
                {
                    var folderRatio = FolderColumnWidth / total;
                    FolderColumnWidth = Clamp(available * folderRatio, 120, 400);
                    NoteColumnWidth = Clamp(available * (1 - folderRatio), 180, 400);
                }
                else if (available > 0)
                {
                    FolderColumnWidth = Clamp(available * 0.44, 120, 400);
                    NoteColumnWidth = Clamp(available - FolderColumnWidth, 180, 400);
                }
                else
                {
                    FolderColumnWidth = 0;
                    NoteColumnWidth = 0;
                }

                if (FolderColumnWidth > 0)
                    _storedFolderColumnWidth = FolderColumnWidth;
                if (NoteColumnWidth > 0)
                    _storedNoteColumnWidth = NoteColumnWidth;
            }
        }
        finally
        {
            _isApplyingLayout = false;
            NotifyNavColumnWidthChanged();
        }
    }

    /// <summary>大纲面板展开后需要刷新内容时触发</summary>
    public event Action? OutlineRefreshRequested;

    /// <summary>更新连接状态展示</summary>
    public void UpdateConnectionStatus() => RefreshConnectionDisplay();

    /// <summary>当前本地待推送变更条数</summary>
    public int GetPendingChangeCount()
    {
        try
        {
            return _changeTracker.GetPendingChangeCount();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>根据认证与同步状态刷新工具栏徽章与底栏文案</summary>
    public void RefreshConnectionDisplay()
    {
        var pendingCount = 0;
        try
        {
            pendingCount = _changeTracker.GetPendingChangeCount();
        }
        catch (Exception ex)
        {
            LogHelper.Debug("读取待推送数量失败: {0}", ex.Message);
        }

        ConnectionDisplay = ConnectionDisplayResolver.Resolve(
            AuthService,
            _currentSyncStatus,
            HasConflicts,
            ConflictCount,
            _lastSyncActivityMessage,
            pendingCount);
        SyncStatusText = ConnectionDisplay.StatusBarText;
    }

    /// <summary>记录引擎活动文案并刷新统一连接展示</summary>
    /// <param name="status">同步引擎状态</param>
    /// <param name="message">活动说明</param>
    public void ApplySyncStatus(SyncStatus status, string? message)
    {
        _currentSyncStatus = status;
        _lastSyncActivityMessage = message;
        RefreshConnectionDisplay();
    }

    /// <summary>更新冲突指示器</summary>
    public void SetConflictCount(int count)
    {
        ConflictCount = count;
        HasConflicts = count > 0;
        RefreshConnectionDisplay();
    }

    /// <summary>同步最大化/还原按钮图标</summary>
    public void UpdateMaximizeState(WindowState state)
    {
        if (state == WindowState.Maximized)
        {
            MaximizeButtonGlyph = "\uE923";
            MaximizeButtonTooltip = "向下还原";
        }
        else
        {
            MaximizeButtonGlyph = "\uE922";
            MaximizeButtonTooltip = "最大化";
        }
    }

    /// <summary>从磁盘布局恢复到 ViewModel 属性</summary>
    public void ApplyLayout(LayoutSettings layout, bool restoreSelection = true)
    {
        _isApplyingLayout = true;

        try
        {
            // 加载布局模式
            var appearance = AppearanceSettings.Load();
            var treeMode = appearance.LayoutMode == NavigationLayoutMode.TreeView;
            IsTreeMode = treeMode;
            FolderTree.IncludeNotes = treeMode;

            _storedFolderColumnWidth = Clamp(layout.FolderColumnWidth, 120, 400);
            _storedNoteColumnWidth = Clamp(layout.NoteColumnWidth, 180, 400);
            _storedOutlineColumnWidth = Clamp(
                layout.OutlineColumnWidth,
                OutlineColumnMinWidthWhenVisible,
                400);
            _storedMergedTreeColumnWidth = Clamp(layout.MergedTreeColumnWidth, 200, 500);

            if (treeMode)
            {
                // 归纳模式：合并树可见，文件夹/笔记列隐藏
                MergedTreeColumnWidth = layout.FolderPanelVisible ? _storedMergedTreeColumnWidth : 0;
                FolderColumnWidth = 0;
                NoteColumnWidth = 0;
            }
            else
            {
                // 并列模式：文件夹/笔记列可见，合并树隐藏
                MergedTreeColumnWidth = 0;
                FolderColumnWidth = layout.FolderPanelVisible ? _storedFolderColumnWidth : 0;
                NoteColumnWidth = layout.FolderPanelVisible ? _storedNoteColumnWidth : 0;
            }

            OutlineColumnWidth = layout.OutlinePanelVisible ? _storedOutlineColumnWidth : 0;

            IsFolderPanelVisible = layout.FolderPanelVisible;
            IsOutlinePanelVisible = layout.OutlinePanelVisible;
            OnPropertyChanged(nameof(OutlineColumnMinWidth));

            _navigationPublisher.NotifyLayoutRebuild();

            if (layout.ExpandedFolderIds.Count > 0)
            {
                Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    FolderTree.RestoreExpandedFolderIds(layout.ExpandedFolderIds);
                }, System.Windows.Threading.DispatcherPriority.Background);
            }

            if (restoreSelection)
            {
                RestoreLastNote(layout);
            }

            if (layout.OutlinePanelVisible)
            {
                OutlineRefreshRequested?.Invoke();
            }

            OnPropertyChanged(nameof(IsSideBySideNavVisible));
            OnPropertyChanged(nameof(IsMergedNavVisible));
            NotifyNavColumnWidthChanged();
        }
        finally
        {
            _isApplyingLayout = false;
        }
    }

    /// <summary>将当前 ViewModel 布局状态写入 LayoutSettings</summary>
    public LayoutSettings CaptureLayout()
    {
        var layout = new LayoutSettings
        {
            FolderPanelVisible = IsFolderPanelVisible,
            NotePanelVisible = true, // 保留向后兼容，并列模式下与文件夹同步显隐
            OutlinePanelVisible = IsOutlinePanelVisible,
            ExpandedFolderIds = FolderTree.GetExpandedFolderIds()
        };

        if (IsFolderPanelVisible)
        {
            if (IsTreeMode && MergedTreeColumnWidth > 0)
                layout.MergedTreeColumnWidth = MergedTreeColumnWidth;
            if (!IsTreeMode && FolderColumnWidth > 0)
                layout.FolderColumnWidth = FolderColumnWidth;
            if (!IsTreeMode && NoteColumnWidth > 0)
                layout.NoteColumnWidth = NoteColumnWidth;
        }
        else
        {
            layout.FolderColumnWidth = _storedFolderColumnWidth;
            layout.NoteColumnWidth = _storedNoteColumnWidth;
            layout.MergedTreeColumnWidth = _storedMergedTreeColumnWidth;
        }

        if (IsOutlinePanelVisible && OutlineColumnWidth > 0)
        {
            layout.OutlineColumnWidth = OutlineColumnWidth;
        }

        var currentNote = Editor.CurrentNote;
        if (currentNote != null)
        {
            layout.LastNoteId = currentNote.Id.ToString();
            layout.LastFolderId = currentNote.FolderId.ToString();
        }

        return layout;
    }

    /// <summary>恢复上次编辑的笔记</summary>
    public void RestoreLastNote(LayoutSettings layout)
    {
        if (layout.LastFolderId == null || layout.LastNoteId == null)
        {
            return;
        }

        if (!Guid.TryParse(layout.LastFolderId, out var folderId))
        {
            return;
        }

        if (!Guid.TryParse(layout.LastNoteId, out var noteId))
        {
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            var folderNode = FolderTree.FindNodeById(FolderTree.RootNodes, folderId);
            if (folderNode == null)
            {
                return;
            }

            FolderTree.SelectedFolder = folderNode.Folder;

            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var note = NoteList.Notes.FirstOrDefault(n => n.Id == noteId);
                if (note != null)
                {
                    NoteList.SelectedNote = note;
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>启动或同步完成后恢复导航选中状态</summary>
    public void FinalizeNavigation(LayoutSettings layout)
    {
        _navigationPublisher.NotifyLayoutRebuild();
        RestoreLastNoteImmediate(layout);

        if (!NoteList.CurrentFolderId.HasValue)
        {
            var folder = FindFirstSelectableFolder();
            if (folder != null)
            {
                FolderTree.SelectedFolder = folder;
                NoteList.LoadNotes(folder.Id);
            }
        }
        else
        {
            _navigationPublisher.NotifyFilterChanged();
        }
    }

    /// <summary>同步恢复上次笔记（同步调用，用于启动流程）</summary>
    private void RestoreLastNoteImmediate(LayoutSettings layout)
    {
        if (layout.LastFolderId == null || layout.LastNoteId == null)
        {
            return;
        }

        if (!Guid.TryParse(layout.LastFolderId, out var folderId))
        {
            return;
        }

        if (!Guid.TryParse(layout.LastNoteId, out var noteId))
        {
            return;
        }

        var folderNode = FolderTree.FindNodeById(FolderTree.RootNodes, folderId);
        if (folderNode == null)
        {
            return;
        }

        FolderTree.SelectedFolder = folderNode.Folder;

        var note = NoteList.Notes.FirstOrDefault(n => n.Id == noteId);
        if (note != null)
        {
            NoteList.SelectedNote = note;
        }
    }

    private LocalFolder? FindFirstSelectableFolder()
    {
        foreach (var item in FolderTree.RootNodes)
        {
            if (item is FolderNode folderNode)
            {
                return folderNode.Folder;
            }
        }

        return null;
    }

    partial void OnIsTreeModeChanged(bool value)
    {
        _navigationLayoutMode.IsTreeMode = value;
        OnPropertyChanged(nameof(IsSideBySideNavVisible));
        OnPropertyChanged(nameof(IsMergedNavVisible));
    }

    partial void OnIsFolderPanelVisibleChanged(bool value)
    {
        if (_isApplyingLayout)
        {
            return;
        }

        if (IsTreeMode)
        {
            // 归纳模式：仅控制合并树列
            if (value)
            {
                if (MergedTreeColumnWidth <= 0)
                    MergedTreeColumnWidth = _storedMergedTreeColumnWidth;
            }
            else
            {
                if (MergedTreeColumnWidth > 0)
                    _storedMergedTreeColumnWidth = MergedTreeColumnWidth;
                MergedTreeColumnWidth = 0;
            }
        }
        else
        {
            // 并列模式：同时控制文件夹列和笔记列
            if (value)
            {
                if (FolderColumnWidth <= 0)
                    FolderColumnWidth = _storedFolderColumnWidth;
                if (NoteColumnWidth <= 0)
                    NoteColumnWidth = _storedNoteColumnWidth;
            }
            else
            {
                if (FolderColumnWidth > 0)
                    _storedFolderColumnWidth = FolderColumnWidth;
                if (NoteColumnWidth > 0)
                    _storedNoteColumnWidth = NoteColumnWidth;
                FolderColumnWidth = 0;
                NoteColumnWidth = 0;
            }
        }

        OnPropertyChanged(nameof(IsSideBySideNavVisible));
        OnPropertyChanged(nameof(IsMergedNavVisible));
        NotifyNavColumnWidthChanged();
    }

    partial void OnFolderColumnWidthChanged(double value)
    {
        if (_isApplyingLayout || IsTreeMode)
        {
            return;
        }

        if (value > 0)
        {
            _storedFolderColumnWidth = value;
        }

        NotifyNavColumnWidthChanged();
    }

    partial void OnNoteColumnWidthChanged(double value)
    {
        if (_isApplyingLayout || IsTreeMode)
        {
            return;
        }

        if (value > 0)
        {
            _storedNoteColumnWidth = value;
        }

        NotifyNavColumnWidthChanged();
    }

    partial void OnMergedTreeColumnWidthChanged(double value)
    {
        if (_isApplyingLayout || !IsTreeMode)
        {
            return;
        }

        if (value > 0)
        {
            _storedMergedTreeColumnWidth = value;
        }

        NotifyNavColumnWidthChanged();
    }

    partial void OnIsOutlinePanelVisibleChanged(bool value)
    {
        if (_isApplyingLayout)
        {
            return;
        }

        if (value)
        {
            if (OutlineColumnWidth < OutlineColumnMinWidthWhenVisible)
            {
                OutlineColumnWidth = Math.Max(_storedOutlineColumnWidth, OutlineColumnMinWidthWhenVisible);
            }

            OutlineRefreshRequested?.Invoke();
        }
        else
        {
            if (OutlineColumnWidth > 0)
            {
                _storedOutlineColumnWidth = OutlineColumnWidth;
            }

            OutlineColumnWidth = 0;
        }

        OnPropertyChanged(nameof(OutlineColumnMinWidth));
    }

    partial void OnOutlineColumnWidthChanged(double value)
    {
        if (_isApplyingLayout)
        {
            return;
        }

        // 可见时不允许拖到最小宽度以下（Grid MinWidth 为主，此处兜底并记忆）
        if (IsOutlinePanelVisible && value > 0 && value < OutlineColumnMinWidthWhenVisible)
        {
            OutlineColumnWidth = OutlineColumnMinWidthWhenVisible;
            return;
        }

        if (value >= OutlineColumnMinWidthWhenVisible)
        {
            _storedOutlineColumnWidth = value;
        }
    }

    [RelayCommand]
    private void ToggleOutlinePanel()
    {
        IsOutlinePanelVisible = !IsOutlinePanelVisible;
    }

    /// <summary>切换布局模式（并列 / 归纳），由外观设置触发</summary>
    public void ApplyLayoutMode(NavigationLayoutMode mode)
    {
        var newTreeMode = mode == NavigationLayoutMode.TreeView;
        if (IsTreeMode == newTreeMode) return;

        _isApplyingLayout = true;
        try
        {
            // 缓存当前列宽
            if (IsTreeMode && MergedTreeColumnWidth > 0)
                _storedMergedTreeColumnWidth = MergedTreeColumnWidth;
            if (!IsTreeMode)
            {
                if (FolderColumnWidth > 0) _storedFolderColumnWidth = FolderColumnWidth;
                if (NoteColumnWidth > 0) _storedNoteColumnWidth = NoteColumnWidth;
            }

            IsTreeMode = newTreeMode;
            FolderTree.IncludeNotes = newTreeMode;

            if (newTreeMode)
            {
                // 切换到归纳模式
                FolderColumnWidth = 0;
                NoteColumnWidth = 0;
                MergedTreeColumnWidth = IsFolderPanelVisible ? _storedMergedTreeColumnWidth : 0;
            }
            else
            {
                // 切换到并列模式
                MergedTreeColumnWidth = 0;
                if (IsFolderPanelVisible)
                {
                    FolderColumnWidth = _storedFolderColumnWidth;
                    NoteColumnWidth = _storedNoteColumnWidth;
                }
            }

            _navigationPublisher.NotifyLayoutRebuild();
            OnPropertyChanged(nameof(IsSideBySideNavVisible));
            OnPropertyChanged(nameof(IsMergedNavVisible));
            NotifyNavColumnWidthChanged();
        }
        finally
        {
            _isApplyingLayout = false;
        }
    }

    [RelayCommand]
    private void MinimizeWindow()
    {
        WindowHost?.Minimize();
    }

    [RelayCommand]
    private void ToggleMaximizeWindow()
    {
        WindowHost?.ToggleMaximize();
    }

    [RelayCommand]
    private void CloseWindow()
    {
        WindowHost?.Close();
    }

    [RelayCommand]
    private async Task OpenSyncSettings() => await _dialogs.OpenSyncSettingsAsync();

    [RelayCommand]
    private void OpenTrash() => _dialogs.OpenTrash();

    [RelayCommand]
    private void OpenBackupManager() => _dialogs.OpenBackupManager();

    [RelayCommand]
    private void OpenNoteSearch() => _dialogs.OpenNoteSearch();

    /// <summary>在导航区定位并滚动到当前打开的笔记</summary>
    [RelayCommand]
    private void RevealActiveNoteInNavigation()
    {
        var note = Editor.CurrentNote;
        if (note == null)
        {
            AppDialog.Info("当前没有打开的笔记。", "定位笔记");
            return;
        }

        if (!string.IsNullOrWhiteSpace(Navigation.SearchText))
        {
            Navigation.ClearSearchFilter();
            NavigationStore.ApplyFilterProjection(IsTreeMode);
        }

        FolderTree.ExpandPathToFolder(note.FolderId);

        var folder = FolderTree.FindFolderById(note.FolderId);
        if (folder != null)
        {
            FolderTree.SelectedFolder = folder;
        }

        if (!NavigationStore.SelectNoteById(note.Id, note.FolderId))
        {
            AppDialog.Warning("无法在导航中找到该笔记，可能已被删除。", "定位笔记");
            return;
        }

        RevealActiveNoteRequested?.Invoke();
    }

    /// <summary>全文搜索：关闭弹窗并在主编辑器打开定位</summary>
    internal void NavigateFromSearch(Models.EditorNavigationIntent intent)
    {
        if (Editor.CurrentNote != null && EditorTextProvider != null)
        {
            Editor.ForceSave(EditorTextProvider());
        }

        Editor.SetPendingNavigation(intent);

        if (!NavigationStore.SelectNoteById(intent.NoteId, intent.FolderId))
        {
            Editor.ClearPendingNavigation();
            AppDialog.Warning(
                "无法打开该笔记，可能已被删除。",
                "全文搜索");
        }
    }

    [RelayCommand]
    private async Task ExportFullLibraryAsync() => await _dialogs.ExportFullLibraryAsync();

    [RelayCommand]
    private void OpenAppearance() => _dialogs.OpenAppearance();

    /// <summary>打开关于与检查更新</summary>
    [RelayCommand]
    private void OpenAbout() => _dialogs.OpenAbout();

    [RelayCommand]
    private void OpenNoteHistory() => _dialogs.OpenNoteHistory();

    [RelayCommand]
    private void OpenConflictDialog() => _dialogs.OpenConflictDialog();

    /// <summary>连接状态入口：有冲突则打开冲突窗，否则打开同步设置</summary>
    [RelayCommand]
    private async Task OpenConnectionStatusAsync()
    {
        if (ConnectionDisplay.OpensConflicts)
        {
            _dialogs.OpenConflictDialog();
            return;
        }

        await _dialogs.OpenSyncSettingsAsync();
    }

    private static double Clamp(double value, double min, double max)
    {
        return Math.Max(min, Math.Min(value, max));
    }
}
