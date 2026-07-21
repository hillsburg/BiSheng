using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.Services.Shell;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Threading;

namespace BiSheng.Latte.ViewModels;

/// <summary>
/// 主视图模型：组装各子 ViewModel 和共享服务（依赖由 LatteHost 组合根注入）
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    public AuthService AuthService { get; }

    /// <summary>由 MainWindow 注入，用于切换文件夹等场景读取编辑器实时文本</summary>
    public Func<string>? EditorTextProvider { get; set; }

    /// <summary>同步引擎（后台运行，负责推送和拉取）</summary>
    public SyncService SyncEngine { get; }

    /// <summary>图片存储服务（本地图片元数据管理）</summary>
    public ImageStorageService ImageStorage { get; }

    /// <summary>图片同步服务（独立于笔记同步，负责图片上传/下载）</summary>
    public ImageSyncService ImageSync { get; }

    /// <summary>同步行为配置（周期、退出 flush 等）</summary>
    public SyncSettings SyncSettings { get; private set; }

    /// <summary>笔记历史版本（本地快照 + 服务端 API）</summary>
    public NoteRevisionService NoteRevisions { get; }

    /// <summary>导出服务（笔记和文件夹导出为 Markdown/Word/PDF）</summary>
    public ExportService Export { get; }

    /// <summary>回收站（软删恢复与过期清理）</summary>
    public TrashService Trash { get; }

    public FolderTreeViewModel FolderTree { get; }
    public NoteListViewModel NoteList { get; }
    public EditorViewModel Editor { get; }
    public NavigationViewModel Navigation { get; }

    /// <summary>导航状态 Store：同步刷新与按 Id 选中笔记</summary>
    public INavigationStore NavigationStore { get; }

    /// <summary>笔记切换事件：UI 层订阅，在 LoadNote 前后设置加载标志</summary>
    public event Action<LocalNote>? NoteSwitching
    {
        add => NavigationStore.NoteSwitching += value;
        remove => NavigationStore.NoteSwitching -= value;
    }

    /// <summary>无笔记选中时清空编辑器</summary>
    public event Action? NoteClosed
    {
        add => NavigationStore.NoteClosed += value;
        remove => NavigationStore.NoteClosed -= value;
    }

    [ObservableProperty]
    private bool _isFolderPanelVisible = true;

    /// <summary>当前是否为归纳模式（笔记在文件夹树内）</summary>
    [ObservableProperty]
    private bool _isTreeMode;

    /// <summary>并列模式下导航区（文件夹 + 笔记两列）是否可见</summary>
    public bool IsSideBySideNavVisible => !IsTreeMode && IsFolderPanelVisible;

    /// <summary>归纳模式下合并树导航列是否可见</summary>
    public bool IsMergedNavVisible => IsTreeMode && IsFolderPanelVisible;

    /// <summary>是否允许尝试同步（凭据完整且开启同步；不表示当前已连通服务器）</summary>
    public bool IsConnected => AuthService.IsConnected;

    /// <summary>顶部状态栏文本</summary>
    [ObservableProperty]
    private string _statusText = "就绪";

    /// <summary>底部同步状态文本</summary>
    [ObservableProperty]
    private string _syncStatusText = "同步状态: --";

    /// <summary>启动加载遮罩是否可见</summary>
    [ObservableProperty]
    private bool _isStartupLoading;

    /// <summary>启动加载遮罩提示文本</summary>
    [ObservableProperty]
    private string _startupStatusText = "正在加载...";

    /// <summary>关闭时同步遮罩是否可见</summary>
    [ObservableProperty]
    private bool _isShutdownLoading;

    /// <summary>关闭时同步遮罩提示文本</summary>
    [ObservableProperty]
    private string _shutdownStatusText = "正在保存并同步数据...";

    /// <summary>启动或关闭加载遮罩是否可见（绑定主窗口遮罩层）</summary>
    public bool IsLoadingOverlayVisible => IsStartupLoading || IsShutdownLoading;

    /// <summary>遮罩层提示文本：关闭优先于启动</summary>
    public string LoadingOverlayText =>
        IsShutdownLoading ? ShutdownStatusText : StartupStatusText;

    partial void OnIsStartupLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoadingOverlayVisible));
        OnPropertyChanged(nameof(LoadingOverlayText));
    }

    partial void OnIsShutdownLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoadingOverlayVisible));
        OnPropertyChanged(nameof(LoadingOverlayText));
    }

    partial void OnStartupStatusTextChanged(string value)
    {
        if (!IsShutdownLoading)
        {
            OnPropertyChanged(nameof(LoadingOverlayText));
        }
    }

    partial void OnShutdownStatusTextChanged(string value)
    {
        if (IsShutdownLoading)
        {
            OnPropertyChanged(nameof(LoadingOverlayText));
        }
    }

    /// <summary>本地变更防抖 Push（2 秒）</summary>
    private System.Timers.Timer? _debouncedPushTimer;

    private readonly object _debouncedPushLock = new();

    private readonly LocalChangeTracker _changeTracker;
    private readonly SignalRService _signalR;
    private readonly INavigationLayoutMode _navigationLayoutMode;
    private readonly INavigationMutationPublisher _navigationPublisher;
    private readonly IDialogNavigationService _dialogs;
    private int _disposeState;

    /// <summary>通过 DI 组合根注入全部依赖</summary>
    public MainViewModel(
        AuthService authService,
        SyncSettings syncSettings,
        LocalEditJournalService editJournal,
        LocalChangeTracker changeTracker,
        SyncService syncEngine,
        ImageStorageService imageStorage,
        ImageSyncService imageSync,
        NoteRevisionService noteRevisions,
        ExportService export,
        TrashService trash,
        FolderTreeViewModel folderTree,
        NoteListViewModel noteList,
        EditorViewModel editor,
        NavigationViewModel navigation,
        INavigationStore navigationStore,
        INavigationLayoutMode navigationLayoutMode,
        INavigationPresentationCoordinator navigationPresentationCoordinator,
        INavigationMutationPublisher navigationPublisher,
        NavigationFilterBridge navigationFilterBridge,
        IDialogNavigationService dialogNavigation,
        SignalRService signalR)
    {
        LogHelper.Info("初始化 MainViewModel");

        AuthService = authService;
        SyncSettings = syncSettings;
        _changeTracker = changeTracker;
        SyncEngine = syncEngine;
        _ = editJournal;
        ImageStorage = imageStorage;
        ImageSync = imageSync;
        NoteRevisions = noteRevisions;
        Export = export;
        Trash = trash;
        FolderTree = folderTree;
        NoteList = noteList;
        Editor = editor;
        Navigation = navigation;
        NavigationStore = navigationStore;
        _signalR = signalR;
        _navigationLayoutMode = navigationLayoutMode;
        _navigationPublisher = navigationPublisher;
        _dialogs = dialogNavigation;
        _ = navigationPresentationCoordinator;
        _ = navigationFilterBridge;
        _navigationLayoutMode.IsTreeMode = IsTreeMode;

        _changeTracker.OnChangeRecorded += OnLocalChangeRecorded;

        FolderTree.SetNoteList(NoteList);
        FolderTree.SetNavigation(Navigation);
        NoteList.SetNavigation(Navigation);

        _signalR.OnReconnected += () => _ = ImageSync.OnNetworkRecoveredAsync();

        SyncEngine.OnSyncStatusChanged += (status, message) =>
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(
                DispatcherPriority.Background,
                () => ApplySyncStatus(status, message));
        };

        // 凭据 / 验证位变化时同步刷新工具栏徽章（不依赖下一次 SyncStatus 事件）
        AuthService.PropertyChanged += OnAuthServicePropertyChanged;

        FolderTree.OnFolderSelected += folderId =>
        {
            if (Editor.CurrentNote != null && EditorTextProvider != null)
            {
                Editor.ForceSave(EditorTextProvider());
            }

            if (NoteList.CurrentFolderId == folderId)
            {
                return;
            }

            NoteList.LoadNotes(folderId);
        };

        NoteList.NoteDeleting += noteId =>
        {
            if (Editor.CurrentNote?.Id != noteId)
            {
                return;
            }

            if (EditorTextProvider != null)
            {
                Editor.ForceSave(EditorTextProvider());
            }

            Editor.ClearNote();
        };

        NoteList.OnNoteTitleChanged += (id, title) => Editor.ApplyRenamedTitle(id, title);

        NoteList.NoteCreated += note =>
        {
            if (IsTreeMode)
            {
                var parentNode = FolderTree.FindNodeById(FolderTree.RootNodes, note.FolderId);
                if (parentNode != null)
                {
                    parentNode.IsExpanded = true;
                }
            }
        };

        StatusText = "就绪";
        RefreshConnectionDisplay();

        _dialogs.BindShell(
            () => WindowHost,
            () => EditorTextProvider?.Invoke(),
            settings => SyncSettings = settings,
            RestartSyncEngineAsync,
            UpdateConnectionStatus,
            RefreshConflictsAfterDialog,
            NavigateFromSearch,
            () => IsConnected);

        LogHelper.Info("MainViewModel 初始化完成");
    }

    /// <summary>Auth 关键字段变化时刷新连接徽章</summary>
    private void OnAuthServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (
            nameof(AuthService.IsServerVerified)
            or nameof(AuthService.IsSyncEnabled)
            or nameof(AuthService.ServerUrl)
            or nameof(AuthService.ApiKey)
            or nameof(AuthService.Username)))
        {
            return;
        }

        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            RefreshConnectionDisplay);
    }

    /// <summary>冲突对话框关闭后刷新冲突计数与导航布局</summary>
    private void RefreshConflictsAfterDialog()
    {
        SetConflictCount(SyncEngine.GetUnresolvedConflictCount());
        _navigationPublisher.NotifyLayoutRebuild();
    }

    /// <summary>启动同步引擎：首次全量同步 + 启动周期轮询 + 连接 SignalR</summary>
    public async Task StartSyncEngineAsync()
    {
        if (!AuthService.IsConnected)
        {
            return;
        }

        await SyncEngine.StartAsync();
        ImageSync.Start();
    }

    /// <summary>启动流程：离线仅加载本地数据；在线模式先同步再恢复导航状态</summary>
    public async Task RunStartupAsync(LayoutSettings layout)
    {
        IsStartupLoading = true;

        try
        {
            if (!AuthService.IsConnected)
            {
                StartupStatusText = AuthService.IsOfflineMode ? "离线模式" : "正在加载本地数据...";
                FinalizeNavigation(layout);
                return;
            }

            StartupStatusText = "正在同步数据...";
            await StartSyncEngineAsync();
            FinalizeNavigation(layout);
        }
        catch (Exception ex)
        {
            LogHelper.Error("启动同步失败", ex);
            StartupStatusText = $"同步失败: {ex.Message}";
            await Task.Delay(1500);
            FinalizeNavigation(layout);
        }
        finally
        {
            IsStartupLoading = false;
        }
    }

    /// <summary>重启同步引擎：设置变更时先停止再启动，或切离线时仅停止</summary>
    public async Task RestartSyncEngineAsync()
    {
        await SyncEngine.StopAsync();
        ImageSync.Stop();

        if (!AuthService.IsConnected)
        {
            AuthService.SetServerVerified(null);
            ApplySyncStatus(
                SyncStatus.Offline,
                AuthService.IsOfflineMode
                    ? "离线模式"
                    : AuthService.HasCredentials && !AuthService.IsSyncEnabled
                        ? "同步已关闭"
                        : "未连接");
            return;
        }

        try
        {
            await SyncEngine.StartAsync();
            ImageSync.Start();
        }
        catch
        {
            // StartAsync 内部已 SetServerVerified(false)；此处勿再覆盖，
            // 以免掩盖 StopAsync 退出 flush / 手动同步刚建立的连通状态
        }

        UpdateConnectionStatus();
    }

    /// <summary>手动触发同步（用户点击"同步"按钮）</summary>
    [RelayCommand]
    private async Task ManualSyncAsync()
    {
        if (AuthService.IsOfflineMode)
        {
            ApplySyncStatus(SyncStatus.Offline, "离线模式，无法同步");
            return;
        }

        if (AuthService.HasCredentials && !AuthService.IsSyncEnabled)
        {
            ApplySyncStatus(SyncStatus.Offline, "同步已关闭，请在「同步与安全 → 连接」中启用同步");
            return;
        }

        if (!AuthService.IsConnected)
        {
            ApplySyncStatus(
                SyncStatus.Offline,
                AuthService.IsOfflineMode
                    ? "离线模式，无法同步"
                    : "同步未启用，无法同步");
            return;
        }

        await SyncEngine.ManualSyncAsync();
    }

    /// <summary>停止同步引擎（窗口关闭时调用）</summary>
    public async Task StopSyncEngineAsync()
    {
        await SyncEngine.StopAsync();
        await ImageSync.FlushPendingUploadsAsync();
        ImageSync.Stop();
    }

    /// <summary>关闭流程：在线且启用退出 flush 时显示遮罩，再推送待同步数据</summary>
    public async Task RunShutdownAsync()
    {
        var showOverlay = AuthService.IsConnected && SyncSettings.FlushOnExit;

        if (showOverlay)
        {
            IsShutdownLoading = true;
            ShutdownStatusText = "正在保存并同步数据...";
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(
                    () => { },
                    DispatcherPriority.Render);
            }
        }

        try
        {
            await StopSyncEngineAsync();
        }
        finally
        {
            if (showOverlay)
            {
                IsShutdownLoading = false;
            }
        }
    }

    [RelayCommand]
    private void ToggleFolderPanel()
    {
        IsFolderPanelVisible = !IsFolderPanelVisible;
    }

    private void OnLocalChangeRecorded()
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            RefreshConnectionDisplay);
        ScheduleDebouncedPush();
    }

    private void ScheduleDebouncedPush()
    {
        if (Volatile.Read(ref _disposeState) != 0 || !AuthService.IsConnected)
        {
            return;
        }

        lock (_debouncedPushLock)
        {
            _debouncedPushTimer ??= new System.Timers.Timer(2000) { AutoReset = false };
            _debouncedPushTimer.Stop();
            _debouncedPushTimer.Elapsed -= OnDebouncedPushElapsed;
            _debouncedPushTimer.Elapsed += OnDebouncedPushElapsed;
            _debouncedPushTimer.Start();
        }
    }

    private void OnDebouncedPushElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (Volatile.Read(ref _disposeState) != 0 || !AuthService.IsConnected)
        {
            return;
        }

        _ = SyncEngine.PushAndPullAsync("变更防抖");
    }

    /// <summary>释放主视图模型持有的防抖定时器和事件订阅</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _changeTracker.OnChangeRecorded -= OnLocalChangeRecorded;
        AuthService.PropertyChanged -= OnAuthServicePropertyChanged;

        lock (_debouncedPushLock)
        {
            if (_debouncedPushTimer == null)
            {
                return;
            }

            _debouncedPushTimer.Stop();
            _debouncedPushTimer.Elapsed -= OnDebouncedPushElapsed;
            _debouncedPushTimer.Dispose();
            _debouncedPushTimer = null;
        }
    }
}
