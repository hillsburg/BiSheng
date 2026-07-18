using System.Net.NetworkInformation;
using BiSheng.Latte.Models;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services.Sync;

/// <summary>
/// SignalR 与网络事件协调：防抖通知、重连与网络恢复触发同步
/// </summary>
internal sealed class SyncSignalRCoordinator : IDisposable
{
    private readonly AuthService _authService;
    private readonly SignalRService _signalR;
    private readonly Func<SyncSettings> _getSettings;
    private readonly Func<bool> _isSyncing;
    private readonly Func<string, Task<bool>> _pushAndPull;
    private readonly Action<SyncStatus, string> _onSyncStatusChanged;

    /// <summary>SignalR 通知防抖间隔</summary>
    private static readonly TimeSpan SignalRNotifyPullDebounce = TimeSpan.FromMilliseconds(300);

    /// <summary>SignalR 通知防抖：合并短时间内多条吹哨为一次 Pull</summary>
    private CancellationTokenSource? _signalRNotifyPullCts;

    /// <summary>协调器是否已释放</summary>
    private int _disposeState;

    /// <summary>创建 SignalR 协调器并订阅事件</summary>
    internal SyncSignalRCoordinator(
        AuthService authService,
        SignalRService signalR,
        Func<SyncSettings> getSettings,
        Func<bool> isSyncing,
        Func<string, Task<bool>> pushAndPull,
        Action<SyncStatus, string> onSyncStatusChanged)
    {
        _authService = authService;
        _signalR = signalR;
        _getSettings = getSettings;
        _isSyncing = isSyncing;
        _pushAndPull = pushAndPull;
        _onSyncStatusChanged = onSyncStatusChanged;

        _signalR.OnChangeReceived += OnRemoteChange;
        _signalR.OnConnectionStateChanged += OnSignalRConnectionChanged;
        _signalR.OnReconnected += OnSignalRReconnected;
        _signalR.OnChangeDeserializationFailed += OnSignalRChangeDeserializationFailed;

        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
    }

    /// <summary>取消 SignalR 防抖 CTS 并释放</summary>
    internal void CancelPendingNotify()
    {
        try
        {
            _signalRNotifyPullCts?.Cancel();
            _signalRNotifyPullCts?.Dispose();
            _signalRNotifyPullCts = null;
        }
        catch (ObjectDisposedException)
        {
            // 忽略
        }
    }

    /// <summary>收到 SignalR 轻量通知：不就地应用（无 Payload），防抖后触发 Push+Pull</summary>
    private void OnRemoteChange(ChangeDto change) => SchedulePullFromSignalRNotify();

    /// <summary>合并短时间内多条通知，避免 NotifyBatch 触发 N 次 Pull</summary>
    private void SchedulePullFromSignalRNotify()
    {
        var previous = Interlocked.Exchange(ref _signalRNotifyPullCts, new CancellationTokenSource());
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 忽略已释放的 CTS
        }

        previous?.Dispose();

        var cts = _signalRNotifyPullCts;
        if (cts == null)
        {
            return;
        }

        _ = DebouncedSignalRNotifyPullAsync(cts.Token);
    }

    /// <summary>防抖结束后执行一次同步；若锁被占用则短暂重试</summary>
    private async Task DebouncedSignalRNotifyPullAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SignalRNotifyPullDebounce, token);
            if (!_authService.IsConnected)
            {
                return;
            }

            // 最多重试几次：PushAndPull 在忙时 WaitAsync(0) 会直接返回 false
            for (var attempt = 0; attempt < 3; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var ok = await _pushAndPull("SignalR 通知");
                if (ok || !_authService.IsConnected)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(400), token);
            }
        }
        catch (OperationCanceledException)
        {
            // 被更新的通知合并取消，属正常
        }
        catch (Exception ex)
        {
            LogHelper.Error("SignalR 通知触发同步失败", ex);
        }
    }

    /// <summary>
    /// SignalR 连接状态变化：断开更新 UI；首次连接由启动同步覆盖，此处不重复 Pull
    /// </summary>
    private void OnSignalRConnectionChanged(bool connected)
    {
        if (connected)
        {
            _onSyncStatusChanged(SyncStatus.Connected, "SignalR 已连接");
        }
        else
        {
            _onSyncStatusChanged(SyncStatus.Error, "连接已断开");
        }
    }

    /// <summary>
    /// SignalR 自动重连成功：无条件版本探测 + Pull，弥补断线期间丢失的推送。
    /// 忙时由 PushAndPull 脏标记补偿，不要在此提前丢弃。
    /// </summary>
    private void OnSignalRReconnected()
    {
        if (!_getSettings().SyncOnNetworkRecover || !_authService.IsConnected)
        {
            return;
        }

        _ = _pushAndPull("SignalR 重连");
    }

    /// <summary>
    /// SignalR 消息反序列化失败：触发补偿 Pull
    /// </summary>
    private void OnSignalRChangeDeserializationFailed()
    {
        if (!_authService.IsConnected)
        {
            return;
        }

        _ = _pushAndPull("SignalR 消息异常补偿");
    }

    /// <summary>
    /// 系统网络可用性变化（网线插拔、WiFi 断连等）
    /// </summary>
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) OnNetworkRecovered();
    }

    /// <summary>系统网络地址变化时尝试恢复同步</summary>
    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        OnNetworkRecovered();
    }

    /// <summary>
    /// 网络恢复时触发同步（忙时由脏标记补偿）
    /// </summary>
    private void OnNetworkRecovered()
    {
        if (!_getSettings().SyncOnNetworkRecover || !_authService.IsConnected)
        {
            return;
        }

        _ = _pushAndPull("网络恢复");
    }

    /// <summary>取消订阅并释放防抖资源</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _signalR.OnChangeReceived -= OnRemoteChange;
        _signalR.OnConnectionStateChanged -= OnSignalRConnectionChanged;
        _signalR.OnReconnected -= OnSignalRReconnected;
        _signalR.OnChangeDeserializationFailed -= OnSignalRChangeDeserializationFailed;

        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;

        CancelPendingNotify();
    }
}
