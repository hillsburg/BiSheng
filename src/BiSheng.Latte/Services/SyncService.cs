using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services.Navigation;
using BiSheng.Latte.Services.Sync;
using BiSheng.Shared;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services;

/// <summary>
/// 同步状态枚举，用于 UI 展示当前同步阶段
/// </summary>
public enum SyncStatus
{
    /// <summary>空闲，无需同步</summary>
    Idle,

    /// <summary>正在推送本地变更到服务端</summary>
    Pushing,

    /// <summary>正在拉取远端变更</summary>
    Pulling,

    /// <summary>SignalR 已连接，实时监听中</summary>
    Connected,

    /// <summary>同步出错（网络断开等）</summary>
    Error,

    /// <summary>离线模式，不进行同步</summary>
    Offline
}

/// <summary>
/// 核心同步引擎（门面）：编排 Push/Pull 管道，暴露公共 API
/// </summary>
public class SyncService : IDisposable
{
    private readonly AuthService _authService;
    private readonly ApiClient _apiClient;
    private readonly SignalRService _signalR;
    private readonly Func<LocalDbContext> _dbFactory;
    private readonly INavigationReadModel _navigationReadModel;
    private SyncSettings _settings;

    private readonly SyncBootstrap _bootstrap;
    private readonly SyncPushPipeline _pushPipeline;
    private readonly SyncPullPipeline _pullPipeline;
    private readonly SyncFullResyncRunner _fullResyncRunner;
    private readonly SyncSignalRCoordinator _signalRCoordinator;

    /// <summary>周期轮询定时器：按配置间隔推送本地待同步队列</summary>
    private System.Timers.Timer? _periodicTimer;

    // ===== 并发控制 =====
    /// <summary>防止多个触发器同时执行同步操作</summary>
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>是否正在同步中</summary>
    private volatile bool _isSyncing;

    /// <summary>同步引擎是否已启动（防止重复初始化）</summary>
    private volatile bool _isStarted;

    /// <summary>资源是否已进入最终释放阶段</summary>
    private int _disposeState;

    /// <summary>上次同步完成时间，用于避免启动时 SignalR 连接回调触发冗余同步</summary>
    private DateTime _lastSyncTime = DateTime.MinValue;

    /// <summary>最近一次 Push/Pull 是否执行了全量重建（导航需 FullRefresh）</summary>
    private bool _lastOperationWasFullResync;

    // ===== 事件 =====

    /// <summary>
    /// 同步状态变更时触发，UI 层订阅此事件来更新状态栏
    /// </summary>
    public event Action<SyncStatus, string>? OnSyncStatusChanged;

    /// <summary>
    /// 检测到未解决的同步冲突时触发，参数为冲突数量
    /// UI 层订阅此事件来显示冲突提示或弹出解决对话框
    /// </summary>
    public event Action<int>? OnConflictsDetected;

    /// <summary>创建同步引擎</summary>
    public SyncService(
        AuthService authService,
        ApiClient apiClient,
        SignalRService signalR,
        Func<LocalDbContext> dbFactory,
        SyncSettings settings,
        INavigationReadModel navigationReadModel,
        LocalEditJournalService? editJournal = null)
    {
        _authService = authService;
        _apiClient = apiClient;
        _signalR = signalR;
        _dbFactory = dbFactory;
        _navigationReadModel = navigationReadModel;
        _settings = settings;

        _bootstrap = new SyncBootstrap(_apiClient, _dbFactory);
        _fullResyncRunner = new SyncFullResyncRunner(
            _apiClient,
            _dbFactory,
            (status, message) => OnSyncStatusChanged?.Invoke(status, message));
        _pushPipeline = new SyncPushPipeline(
            _apiClient,
            _dbFactory,
            editJournal,
            count => OnConflictsDetected?.Invoke(count),
            () => _fullResyncRunner.PerformFullResyncAsync(),
            () => _lastOperationWasFullResync = true);
        _pullPipeline = new SyncPullPipeline(
            _apiClient,
            _dbFactory,
            count => OnConflictsDetected?.Invoke(count),
            () => _fullResyncRunner.PerformFullResyncAsync());
        _signalRCoordinator = new SyncSignalRCoordinator(
            _authService,
            _signalR,
            () => _settings,
            () => _isSyncing,
            PushAndPullAsync,
            (status, message) => OnSyncStatusChanged?.Invoke(status, message));

        _periodicTimer = new System.Timers.Timer(_settings.PeriodicPushIntervalMs)
        {
            AutoReset = true
        };
        _periodicTimer.Elapsed += (_, _) => OnPeriodicTick();
    }

    /// <summary>应用新的同步配置（保存设置后调用，运行中更新定时器间隔）</summary>
    public void ApplySettings(SyncSettings settings)
    {
        ThrowIfDisposed();
        _settings = settings;
        if (_periodicTimer != null)
        {
            _periodicTimer.Interval = settings.PeriodicPushIntervalMs;
        }
    }

    // ========================================================
    //  公共方法
    // ========================================================

    /// <summary>
    /// 启动同步引擎：首次全量同步 + 启动周期轮询 + 连接 SignalR
    /// 可安全重复调用（内部检查 _isStarted 标志）
    /// </summary>
    public async Task StartAsync()
    {
        ThrowIfDisposed();
        if (_isStarted) return;

        if (!_authService.IsConnected)
        {
            LogHelper.Warn("同步引擎启动失败：未连接服务器");
            OnSyncStatusChanged?.Invoke(SyncStatus.Offline, "离线模式");
            return;
        }

        try
        {
            _isStarted = true;
            LogHelper.Info("同步引擎启动");

            // 注意：不得在启动时按「pending.Payload == 本地实体」清理队列。
            // 正常未推送的 Create/Update 本来就与本地一致；Delete 时本地亦已软删。
            // 误清会导致断线编辑后重连/重启引擎丢失待推送变更。

            // 上次全量重建若在清库后中断，磁盘抢救文件仍在：先完成重建再走常规同步
            if (FullResyncRescueStore.Exists())
            {
                LogHelper.Warn("检测到未完成的全量重建抢救文件，正在恢复");
                await _fullResyncRunner.PerformFullResyncAsync();
            }

            await _bootstrap.EnsureFreshLocalReplicaAsync();
            await _bootstrap.EnsureInitialSyncAsync();

            // 忙时 PushAndPull 会立即返回 false；启动阶段应短暂重试，避免误判为无法连接
            var startedSync = false;
            for (var attempt = 0; attempt < 8; attempt++)
            {
                if (await PushAndPullAsync("启动同步"))
                {
                    startedSync = true;
                    break;
                }

                await Task.Delay(250);
            }

            if (!startedSync)
            {
                throw new InvalidOperationException("无法与服务器同步");
            }

            // Push/Pull 已证明连通，勿再做会失败覆盖的二次探测
            _authService.SetServerVerified(true);

            await _signalR.ConnectAsync();

            _periodicTimer?.Start();
            LogHelper.Info("同步引擎已就绪（SignalR + 周期轮询）");
            OnSyncStatusChanged?.Invoke(SyncStatus.Connected, "同步引擎已就绪");

            // 尽力刷新用户名，失败不影响已成功的启动
            _ = TryRefreshUsernameAfterStartAsync();
        }
        catch (Exception ex)
        {
            _isStarted = false;
            _periodicTimer?.Stop();
            try
            {
                await _signalR.DisconnectAsync();
            }
            catch (Exception disconnectEx)
            {
                LogHelper.Warn("启动失败后断开 SignalR 时出错: {0}", disconnectEx.Message);
            }

            _authService.SetServerVerified(false);
            OnSyncStatusChanged?.Invoke(SyncStatus.Error, $"同步失败: {ApiClientException.GetUserMessage(ex)}");
            LogHelper.Error("同步引擎启动失败", ex);
            throw;
        }
    }

    /// <summary>启动成功后异步刷新 Username，不反向改写验证状态</summary>
    private async Task TryRefreshUsernameAfterStartAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_authService.ServerUrl)
                || string.IsNullOrWhiteSpace(_authService.ApiKey))
            {
                return;
            }

            var result = await AuthService.ProbeConnectionAsync(
                _authService.ServerUrl!,
                _authService.ApiKey!);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Username))
            {
                _authService.Username = result.Username;
            }
        }
        catch (Exception ex)
        {
            LogHelper.Warn("启动后刷新用户名失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 停止同步引擎：可选先 flush 待推送队列，再停止定时器、断开 SignalR。
    /// flush 时会等待进行中的同步结束并排空 pending，避免忙时空跑后立刻备份。
    /// </summary>
    public async Task StopAsync(bool? flushPending = null)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        _periodicTimer?.Stop();
        Interlocked.Exchange(ref _suppressBackgroundCompensation, 1);

        try
        {
            var flush = flushPending ?? _settings.FlushOnExit;
            if (flush && _authService.IsConnected)
            {
                await DrainSyncForShutdownAsync();
            }

            _isStarted = false;
            await _signalR.DisconnectAsync();
        }
        finally
        {
            Interlocked.Exchange(ref _suppressBackgroundCompensation, 0);
        }
    }

    /// <summary>
    /// 周期 tick：有待推送则 Push；SignalR 断开或连接态版本探测则 Pull 补偿
    /// </summary>
    private void OnPeriodicTick()
    {
        if (Volatile.Read(ref _disposeState) != 0
            || !_authService.IsConnected
            || !_isStarted)
        {
            return;
        }

        using var db = _dbFactory();
        var hasPending = db.PendingChanges.Any();

        if (hasPending || !_signalR.IsConnected || _settings.PeriodicVersionProbeWhenConnected)
        {
            var trigger = hasPending ? "周期轮询"
                : !_signalR.IsConnected ? "周期轮询(离线补偿)"
                : "周期版本探测";
            // 忙时 PushAndPull 会置脏标记并在结束后补偿
            _ = PushAndPullAsync(trigger);
        }
    }

    /// <summary>
    /// 手动触发同步（用户点击"同步"按钮时调用）
    /// 不受防抖限制，立即执行
    /// </summary>
    public Task<bool> ManualSyncAsync() => PushAndPullAsync("手动同步");

    /// <summary>
    /// 应用从后台唤醒时调用（Window.Activated 事件）
    /// 检查是否有遗漏的变更需要同步
    /// </summary>
    public void OnAppActivated()
    {
        if (!_settings.SyncOnAppActivated || !_authService.IsConnected)
        {
            return;
        }

        // 忙时由脏标记补偿
        _ = PushAndPullAsync("应用唤醒");
    }

    // ========================================================
    //  冲突管理
    // ========================================================

    /// <summary>
    /// 冲突解决后，将结果重新加入待推送队列（使用 LocalPendingChange 独立表）
    /// 去重合并：同一实体的旧记录会被替换；本地已软删时排队 Delete，避免复活远端实体。
    /// </summary>
    private void EnqueueResolvedChange(LocalDbContext db, string entityType, Guid entityId, DateTime updatedAt)
    {
        var existing = db.PendingChanges.FirstOrDefault(p => p.EntityType == entityType && p.EntityId == entityId);
        if (existing != null)
        {
            db.PendingChanges.Remove(existing);
        }

        string action = ChangeActions.Update;
        string? payloadJson = null;

        if (entityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(entityId);
            if (note == null)
            {
                return;
            }

            if (note.IsDeleted)
            {
                action = ChangeActions.Delete;
            }
            else
            {
                payloadJson = SyncPayloadJson.Serialize(
                    SyncPayloadBuilder.Note(note.Title, note.Content, note.FolderId, note.IsFavorite, note.IsPinned));
            }
        }
        else if (entityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(entityId);
            if (folder == null)
            {
                return;
            }

            if (folder.IsDeleted)
            {
                action = ChangeActions.Delete;
            }
            else
            {
                payloadJson = SyncPayloadJson.Serialize(
                    SyncPayloadBuilder.Folder(folder.Name, folder.ParentId, folder.IsFavorite, folder.IsPinned));
            }
        }
        else
        {
            return;
        }

        db.PendingChanges.Add(new LocalPendingChange
        {
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            Payload = payloadJson,
            UpdatedAt = updatedAt
        });
    }

    /// <summary>获取所有未解决的同步冲突</summary>
    public List<SyncConflict> GetUnresolvedConflicts()
    {
        using var db = _dbFactory();
        return db.SyncConflicts.Where(c => !c.IsResolved).ToList();
    }

    /// <summary>获取未解决冲突的数量</summary>
    public int GetUnresolvedConflictCount()
    {
        using var db = _dbFactory();
        return db.SyncConflicts.Count(c => !c.IsResolved);
    }

    /// <summary>
    /// 解决冲突：保留本地版本，丢弃远端版本
    /// 本地内容已在 DB 中，无需修改；标记冲突为已解决，并将本地版本加入待推送队列
    /// （含本地 Delete，避免误发 Update 复活远端）
    /// </summary>
    public void ResolveKeepLocal(int conflictId)
    {
        using var db = _dbFactory();
        var conflict = db.SyncConflicts.Find(conflictId);
        if (conflict == null)
        {
            return;
        }

        conflict.IsResolved = true;
        db.SyncConflicts.Update(conflict);

        EnqueueResolvedChange(db, conflict.EntityType, conflict.EntityId, conflict.LocalUpdatedAt);

        db.SaveChangesWithLock();
        _ = PushAndPullAsync("冲突解决");
    }

    /// <summary>
    /// 解决冲突：采用远端版本，覆盖本地内容
    /// 使用完整 RemotePayload；远端为 Delete 时软删本地实体
    /// </summary>
    public void ResolveKeepRemote(int conflictId)
    {
        using var db = _dbFactory();
        var conflict = db.SyncConflicts.Find(conflictId);
        if (conflict == null)
        {
            return;
        }

        if (conflict.RemoteAction == ChangeActions.Delete)
        {
            SyncRemoteChangeMerger.ApplyRemoteChangeToLocalDb(db, new ChangeDto
            {
                EntityType = conflict.EntityType,
                EntityId = conflict.EntityId,
                Action = ChangeActions.Delete,
                Timestamp = conflict.RemoteUpdatedAt,
                Payload = null
            });
        }
        else if (conflict.EntityType == EntityTypes.Note)
        {
            // 优先完整 payload；旧冲突仅有展示正文时回退 RemoteContent
            var payload = !string.IsNullOrEmpty(conflict.RemotePayload)
                ? conflict.RemotePayload
                : conflict.RemoteContent;
            SyncRemoteChangeMerger.ApplyRemoteNotePayload(db, conflict.EntityId, payload, conflict.RemoteUpdatedAt);

            // 采用远端非删除时，清除本地软删标记
            var note = db.Notes.Find(conflict.EntityId);
            if (note != null)
            {
                note.IsDeleted = false;
                note.DeletedAt = null;
            }
        }
        else if (conflict.EntityType == EntityTypes.Folder)
        {
            var payload = !string.IsNullOrEmpty(conflict.RemotePayload)
                ? conflict.RemotePayload
                : conflict.RemoteContent;
            SyncRemoteChangeMerger.ApplyRemoteFolderPayload(db, conflict.EntityId, payload, conflict.RemoteUpdatedAt);

            var folder = db.Folders.Find(conflict.EntityId);
            if (folder != null)
            {
                folder.IsDeleted = false;
                folder.DeletedAt = null;
            }
        }

        conflict.IsResolved = true;
        db.SyncConflicts.Update(conflict);
        db.SaveChangesWithLock();

        PublishNavigationReadModel(SyncNavigationDelta.FullRefresh);
    }

    /// <summary>
    /// 解决冲突：用户手动合并后的内容
    /// 将合并结果写入本地 DB，并记录变更到待推送队列，确保推送到服务端
    /// </summary>
    public void ResolveMerged(int conflictId, string mergedContent)
    {
        using var db = _dbFactory();
        var conflict = db.SyncConflicts.Find(conflictId);
        if (conflict == null) return;

        if (conflict.EntityType == EntityTypes.Note)
        {
            var note = db.Notes.Find(conflict.EntityId);
            if (note != null)
            {
                note.Content = mergedContent;
                note.UpdatedAt = DateTime.UtcNow;
            }
        }
        else if (conflict.EntityType == EntityTypes.Folder)
        {
            var folder = db.Folders.Find(conflict.EntityId);
            if (folder != null)
            {
                folder.Name = mergedContent;
                folder.UpdatedAt = DateTime.UtcNow;
            }
        }

        conflict.IsResolved = true;
        db.SyncConflicts.Update(conflict);

        EnqueueResolvedChange(db, conflict.EntityType, conflict.EntityId, DateTime.UtcNow);

        db.SaveChangesWithLock();
        _ = PushAndPullAsync("冲突解决");
    }

    // ========================================================
    //  核心同步流程
    // ========================================================

    /// <summary>忙时丢弃的同步请求：结束后再跑一轮补偿</summary>
    private int _syncNeeded;

    /// <summary>退出 flush 期间抑制 fire-and-forget 补偿，避免 StopAsync 返回后仍在写库</summary>
    private int _suppressBackgroundCompensation;

    /// <summary>退出 flush 等待进行中同步的最长时间</summary>
    private static readonly TimeSpan ShutdownSyncWait = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Push + Pull：推送本地待推送变更 → 版本感知 → 拉取远端变更
    /// 带并发锁；忙时置脏标记，结束后自动补偿一次
    /// </summary>
    /// <param name="trigger">触发来源（日志/状态文案）</param>
    /// <param name="waitIfBusy">为 true 时等待进行中的同步结束再执行（退出 flush）</param>
    /// <returns>是否同步成功；失败时不抛出异常（供周期轮询等场景）</returns>
    public Task<bool> PushAndPullAsync(string trigger) => PushAndPullAsync(trigger, waitIfBusy: false);

    /// <summary>Push + Pull，可选等待忙锁</summary>
    public async Task<bool> PushAndPullAsync(string trigger, bool waitIfBusy)
    {
        if (Volatile.Read(ref _disposeState) != 0 || !_authService.IsConnected)
        {
            return false;
        }

        var lockWait = waitIfBusy ? ShutdownSyncWait : TimeSpan.Zero;
        if (!await _syncLock.WaitAsync(lockWait))
        {
            Volatile.Write(ref _syncNeeded, 1);
            return false;
        }

        _isSyncing = true;

        try
        {
            OnSyncStatusChanged?.Invoke(SyncStatus.Pushing, $"[{trigger}] 推送中...");
            var hadPush = await _pushPipeline.PushPendingChangesAsync();

            long serverVersion;
            if (hadPush)
            {
                using var pushDb = _dbFactory();
                serverVersion = pushDb.SyncState.FirstOrDefault()?.LastSyncVersion ?? 0;
            }
            else
            {
                serverVersion = await _apiClient.GetAsync<long>("/api/sync/version");
            }

            using var checkDb = _dbFactory();
            var localVersion = checkDb.SyncState.FirstOrDefault()?.LastSyncVersion ?? 0;
            var remainingPending = checkDb.PendingChanges.Any();

            if (localVersion >= serverVersion)
            {
                _lastSyncTime = DateTime.UtcNow;
                // 成功跑完 Push/Pull 即证明连通（含仅版本探测、无待推送的情况）
                _authService.SetServerVerified(true);

                var statusMessage = remainingPending
                    ? $"[{trigger}] 同步完成（仍有待推送，将重试）"
                    : hadPush
                        ? $"[{trigger}] 已同步"
                        : $"[{trigger}] 已同步（无新变更）";
                OnSyncStatusChanged?.Invoke(SyncStatus.Connected, statusMessage);
                if (hadPush)
                {
                    PublishNavigationReadModel(_lastOperationWasFullResync
                        ? SyncNavigationDelta.FullRefresh
                        : SyncNavigationDelta.Empty);
                    _lastOperationWasFullResync = false;
                }

                LogHelper.Debug("同步完成: {0}", trigger);
                return true;
            }

            OnSyncStatusChanged?.Invoke(SyncStatus.Pulling, $"[{trigger}] 拉取中（落后 {serverVersion - localVersion} 个版本）...");
            var delta = await _pullPipeline.PullChangesAsync();

            _lastSyncTime = DateTime.UtcNow;
            _authService.SetServerVerified(true);
            OnSyncStatusChanged?.Invoke(SyncStatus.Connected, $"[{trigger}] 已同步");
            PublishNavigationReadModel(delta);
            LogHelper.Debug("同步完成: {0}", trigger);
            return true;
        }
        catch (Exception ex)
        {
            OnSyncStatusChanged?.Invoke(SyncStatus.Error, $"同步失败: {ApiClientException.GetUserMessage(ex)}");
            LogHelper.Error($"同步失败 [{trigger}]", ex);
            return false;
        }
        finally
        {
            _isSyncing = false;
            _syncLock.Release();

            // 忙时收到的重连/通知：结束后再补偿一轮（退出 flush 期间改为由 Drain 同步排空）
            if (Interlocked.Exchange(ref _syncNeeded, 0) != 0
                && Volatile.Read(ref _suppressBackgroundCompensation) == 0
                && Volatile.Read(ref _disposeState) == 0
                && _authService.IsConnected)
            {
                _ = PushAndPullAsync("补偿同步");
            }
        }
    }

    /// <summary>
    /// 退出前排空：等待进行中的同步，再多次 Push/Pull，直到无 pending 或超时。
    /// </summary>
    private async Task DrainSyncForShutdownAsync()
    {
        var deadline = DateTime.UtcNow + ShutdownSyncWait;
        var attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            attempt++;
            Interlocked.Exchange(ref _syncNeeded, 0);
            var ok = await PushAndPullAsync($"退出同步#{attempt}", waitIfBusy: true);

            using var db = _dbFactory();
            var hasPending = db.PendingChanges.Any();
            if (!hasPending)
            {
                LogHelper.Info("退出同步排空完成（共 {0} 轮）", attempt);
                return;
            }

            if (!ok)
            {
                LogHelper.Warn("退出同步排空未成功，仍有待推送变更");
                return;
            }
        }

        LogHelper.Warn("退出同步排空超时，仍可能有未推送变更");
    }

    // ========================================================
    //  测试 shim
    // ========================================================

    /// <summary>测试用：合并远端变更</summary>
    internal static bool MergeRemoteChange(LocalDbContext db, ChangeDto change)
        => SyncRemoteChangeMerger.MergeRemoteChange(db, change);

    /// <summary>测试用：排序待推送队列</summary>
    internal static (List<LocalPendingChange> Sorted, HashSet<Guid> CyclicIds) OrderPendingForPush(
        List<LocalPendingChange> pending)
        => SyncPendingOrdering.OrderPendingForPush(pending);

    // ========================================================
    //  资源释放
    // ========================================================

    /// <summary>本地 DB 写入完成后发布导航读模型变更（与 UI 解耦）</summary>
    private void PublishNavigationReadModel(SyncNavigationDelta delta)
    {
        _navigationReadModel.Publish(NavigationProjectionUpdate.FromDelta(delta));
    }

    /// <summary>确认同步引擎尚未进入最终释放阶段</summary>
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(SyncService));
        }
    }

    /// <summary>
    /// 停止同步引擎并释放资源
    /// 确保当前同步操作完成后才释放锁，避免 Dispose 时序竞态
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _isStarted = false;
        _periodicTimer?.Stop();
        _signalRCoordinator.Dispose();
        _periodicTimer?.Dispose();
        _periodicTimer = null;

        // 最多等待正在执行的同步结束，避免释放仍在使用的并发锁。
        if (_syncLock.Wait(5000))
        {
            _syncLock.Dispose();
        }
    }
}
