using System.Windows;
using System.Windows.Threading;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Services.Navigation;

/// <summary>
/// 读模型 → UI 的唯一入口：按投影类型分发，统一 deferred 刷新与编辑会话保护。
/// </summary>
public sealed class NavigationPresentationCoordinator : INavigationPresentationCoordinator, IDisposable
{
    private readonly INavigationReadModel _readModel;
    private readonly INavigationStore _navigationStore;
    private readonly IEditorSessionService _editorSession;
    private readonly INavigationLayoutMode _layoutMode;
    private readonly FolderTreeViewModel _folderTree;
    private readonly EditorViewModel _editor;
    private DispatcherTimer? _deferredTimer;
    private NavigationProjectionUpdate? _pendingUpdate;
    private int _disposeState;

    /// <summary>订阅读模型并绑定编辑器会话生命周期</summary>
    public NavigationPresentationCoordinator(
        INavigationReadModel readModel,
        INavigationStore navigationStore,
        IEditorSessionService editorSession,
        INavigationLayoutMode layoutMode,
        FolderTreeViewModel folderTree,
        EditorViewModel editor)
    {
        _readModel = readModel;
        _navigationStore = navigationStore;
        _editorSession = editorSession;
        _layoutMode = layoutMode;
        _folderTree = folderTree;
        _editor = editor;

        readModel.Changed += OnNavigationReadModelChanged;

        navigationStore.NoteSwitching += OnNoteSwitching;
        navigationStore.NoteClosed += OnNoteClosed;
    }

    /// <summary>笔记切换时通知编辑会话</summary>
    private void OnNoteSwitching(LocalNote note)
    {
        _editorSession.NotifyNoteOpened(note.Id, note.Version);
    }

    /// <summary>笔记关闭时通知编辑会话</summary>
    private void OnNoteClosed()
    {
        _editorSession.NotifyNoteClosed();
    }

    private void OnNavigationReadModelChanged(NavigationProjectionUpdate update)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            HandleProjectionUpdate(update);
            return;
        }

        Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => HandleProjectionUpdate(update));
    }

    private void HandleProjectionUpdate(NavigationProjectionUpdate update)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        switch (update.Kind)
        {
            case NavigationProjectionKind.FilterOnly:
                _navigationStore.ApplyFilterProjection(_layoutMode.IsTreeMode);
                return;

            case NavigationProjectionKind.LayoutRebuild:
                _navigationStore.ApplyLayoutRebuild(_layoutMode.IsTreeMode);
                return;

            default:
                HandleDataChange(update);
                break;
        }
    }

    private void HandleDataChange(NavigationProjectionUpdate update)
    {
        if (IsPresentationBlocked())
        {
            MergePendingUpdate(update);
            ScheduleDeferredApply();
            return;
        }

        ApplyDataUpdate(update);
    }

    private void ApplyDataUpdate(NavigationProjectionUpdate update)
    {
        _navigationStore.ApplyRemoteDelta(update.Delta, _layoutMode.IsTreeMode);
        _editorSession.ApplyRemoteChanges(update.Delta);
    }

    private bool IsPresentationBlocked() =>
        _editor.IsEditingSessionActive || _folderTree.IsInlineRenamingActive;

    private void MergePendingUpdate(NavigationProjectionUpdate update)
    {
        if (_pendingUpdate == null)
        {
            _pendingUpdate = update;
            return;
        }

        if (update.Kind != NavigationProjectionKind.DataChange
            || _pendingUpdate.Kind != NavigationProjectionKind.DataChange)
        {
            _pendingUpdate = NavigationProjectionUpdate.FullDataRebuild;
            return;
        }

        if (update.Delta.RequiresFullRefresh || _pendingUpdate.Delta.RequiresFullRefresh)
        {
            _pendingUpdate = NavigationProjectionUpdate.FullDataRebuild;
            return;
        }

        var merged = _pendingUpdate.Delta.Changes.Concat(update.Delta.Changes).ToList();
        _pendingUpdate = NavigationProjectionUpdate.FromDelta(SyncNavigationDelta.FromChanges(merged));
    }

    private void ScheduleDeferredApply()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        _deferredTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _deferredTimer.Stop();
        _deferredTimer.Tick -= OnDeferredApplyTick;
        _deferredTimer.Tick += OnDeferredApplyTick;
        _deferredTimer.Start();
    }

    private void OnDeferredApplyTick(object? sender, EventArgs e)
    {
        if (IsPresentationBlocked())
        {
            ScheduleDeferredApply();
            return;
        }

        _deferredTimer?.Stop();
        var update = _pendingUpdate ?? NavigationProjectionUpdate.Empty;
        ApplyDataUpdate(update);
        _pendingUpdate = null;
    }

    /// <summary>停止延迟刷新并取消导航事件订阅</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _readModel.Changed -= OnNavigationReadModelChanged;
        _navigationStore.NoteSwitching -= OnNoteSwitching;
        _navigationStore.NoteClosed -= OnNoteClosed;

        if (_deferredTimer != null)
        {
            _deferredTimer.Stop();
            _deferredTimer.Tick -= OnDeferredApplyTick;
            _deferredTimer = null;
        }

        _pendingUpdate = null;
    }
}
