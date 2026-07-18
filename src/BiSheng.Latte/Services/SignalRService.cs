using System.Text.Json;
using BiSheng.Shared.Sync;
using Microsoft.AspNetCore.SignalR.Client;

namespace BiSheng.Latte.Services;

/// <summary>
/// SignalR 实时同步连接管理
/// </summary>
public class SignalRService : IDisposable
{
    private HubConnection? _connection;
    private readonly AuthService _authService;
    private int _disposeState;

    /// <summary>收到服务端变更推送时触发</summary>
    public event Action<ChangeDto>? OnChangeReceived;

    /// <summary>连接状态变化时触发（含首次连接）</summary>
    public event Action<bool>? OnConnectionStateChanged;

    /// <summary>自动重连成功时触发（不含首次 StartAsync 连接）</summary>
    public event Action? OnReconnected;

    /// <summary>OnChange 反序列化失败时触发，供同步引擎补偿 Pull</summary>
    public event Action? OnChangeDeserializationFailed;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public SignalRService(AuthService authService)
    {
        _authService = authService;
    }

    public async Task ConnectAsync()
    {
        ThrowIfDisposed();

        if (_connection != null)
            await DisconnectAsync();

        if (string.IsNullOrEmpty(_authService.ApiKey) || string.IsNullOrEmpty(_authService.ServerUrl)) return;

        _connection = new HubConnectionBuilder()
            .WithUrl($"{_authService.ServerUrl}/hubs/sync", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(_authService.ApiKey);
            })
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15) })
            .Build();

        _connection.On<JsonElement>("OnChange", element =>
        {
            try
            {
                var change = JsonSerializer.Deserialize<ChangeDto>(element.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (change != null)
                    OnChangeReceived?.Invoke(change);
            }
            catch (Exception ex)
            {
                LogHelper.Warn("SignalR 变更反序列化失败: {0}", ex.Message);
                OnChangeDeserializationFailed?.Invoke();
            }
        });

        _connection.Reconnected += _ =>
        {
            OnConnectionStateChanged?.Invoke(true);
            OnReconnected?.Invoke();
            return Task.CompletedTask;
        };

        _connection.Closed += _ =>
        {
            OnConnectionStateChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        try
        {
            await _connection.StartAsync();
            OnConnectionStateChanged?.Invoke(true);
        }
        catch
        {
            OnConnectionStateChanged?.Invoke(false);
        }
    }

    public async Task DisconnectAsync()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (_connection != null)
        {
            try { await _connection.StopAsync(); } catch { }
            try { await _connection.DisposeAsync(); } catch { }
            _connection = null;
        }
        OnConnectionStateChanged?.Invoke(false);
    }

    /// <summary>确认实时连接服务尚未进入最终释放阶段</summary>
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(SignalRService));
        }
    }

    /// <summary>释放最终 SignalR 连接和事件订阅</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        var connection = Interlocked.Exchange(ref _connection, null);
        if (connection != null)
        {
            try
            {
                connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                LogHelper.Warn("释放 SignalR 连接失败: {0}", ex.Message);
            }
        }

        OnChangeReceived = null;
        OnConnectionStateChanged = null;
        OnReconnected = null;
        OnChangeDeserializationFailed = null;
    }
}
