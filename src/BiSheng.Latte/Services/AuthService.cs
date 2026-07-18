using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BiSheng.Latte.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiSheng.Latte.Services;

/// <summary>
/// 认证服务：管理服务器地址、API Key 与是否启用同步，支持离线模式。
/// API Key 以 Windows DPAPI（CurrentUser）加密后写入 config.json。
/// </summary>
public partial class AuthService : ObservableObject
{
    private const string DpapiPrefix = "dpapi:";
    private readonly string _configFilePath;

    /// <summary>服务器地址（为 null 或空时表示离线模式）</summary>
    [ObservableProperty]
    private string? _serverUrl;

    /// <summary>API Key（用于客户端认证）</summary>
    [ObservableProperty]
    private string? _apiKey;

    /// <summary>连接的用户名（从服务端获取）</summary>
    [ObservableProperty]
    private string? _username;

    /// <summary>是否启用与服务器同步（false = 用户关闭同步，凭据仍保留）</summary>
    [ObservableProperty]
    private bool _isSyncEnabled = true;

    /// <summary>最近一次与服务端连通性验证结果；null 表示尚未验证或凭据已变更</summary>
    [ObservableProperty]
    private bool? _isServerVerified;

    /// <summary>是否为离线模式（服务器地址或 API Key 任一未配置）</summary>
    public bool IsOfflineMode =>
        string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>是否已配置完整服务器凭据（地址与 API Key 均非空）</summary>
    public bool HasCredentials => !IsOfflineMode;

    /// <summary>是否允许尝试同步（凭据完整且用户开启同步；不表示当前能连上服务器）</summary>
    public bool IsConnected => HasCredentials && IsSyncEnabled;

    partial void OnServerUrlChanged(string? value) => IsServerVerified = null;

    partial void OnApiKeyChanged(string? value) => IsServerVerified = null;

    public AuthService()
    {
        LatteAppPaths.EnsureRoot();
        _configFilePath = LatteAppPaths.ConfigFile;
        LoadConfig();
    }

    /// <summary>供设置页、同步页展示的人类可读连接状态</summary>
    public string GetConnectionStatusDescription() =>
        ConnectionDisplayResolver.Resolve(this, SyncStatus.Idle, hasConflicts: false).DetailText;

    /// <summary>记录与服务端的连通性验证结果</summary>
    public void SetServerVerified(bool? verified)
    {
        IsServerVerified = verified;
    }

    /// <summary>探测当前已保存凭据并更新 <see cref="IsServerVerified"/></summary>
    public async Task<bool> ConfirmServerConnectionAsync()
    {
        if (!IsConnected)
        {
            SetServerVerified(null);
            return false;
        }

        var result = await ProbeConnectionAsync(ServerUrl!, ApiKey!);
        if (result.Success)
        {
            Username = result.Username;
            SetServerVerified(true);
            return true;
        }

        SetServerVerified(false);
        return false;
    }

    /// <summary>
    /// 测试连接服务端，验证 API Key 是否有效（使用当前已保存凭据）
    /// </summary>
    public async Task<bool> TestConnectionAsync()
    {
        if (IsOfflineMode)
        {
            SetServerVerified(false);
            return false;
        }

        var result = await ProbeConnectionAsync(ServerUrl!, ApiKey!);
        if (result.Success)
        {
            Username = result.Username;
            SetServerVerified(true);
            return true;
        }

        SetServerVerified(false);
        return false;
    }

    /// <summary>
    /// 探测指定凭据能否连通服务器；不修改当前 AuthService 的地址、Key 与验证状态
    /// </summary>
    public static async Task<ConnectionProbeResult> ProbeConnectionAsync(string serverUrl, string apiKey)
    {
        if (string.IsNullOrWhiteSpace(serverUrl) || string.IsNullOrWhiteSpace(apiKey))
        {
            return ConnectionProbeResult.Failed;
        }

        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(serverUrl.Trim()), Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.Add("X-Api-Key", apiKey.Trim());

            var response = await http.GetAsync("/api/auth/verify-key");
            if (!response.IsSuccessStatusCode)
            {
                return ConnectionProbeResult.Failed;
            }

            var json = await response.Content.ReadAsStringAsync();
            var verifyResult = JsonSerializer.Deserialize<VerifyKeyResult>(json);
            return new ConnectionProbeResult(true, verifyResult?.username);
        }
        catch
        {
            return ConnectionProbeResult.Failed;
        }
    }

    /// <summary>保存配置到文件（API Key 必须 DPAPI 加密成功，禁止明文回退）</summary>
    public void SaveConfig()
    {
        string? protectedKey;
        try
        {
            protectedKey = ProtectSecret(ApiKey);
        }
        catch (CryptographicException ex)
        {
            LogHelper.Error("API Key DPAPI 加密失败，已拒绝明文写入配置", ex);
            throw new InvalidOperationException(
                "无法加密保存 API Key（Windows DPAPI）。请确认以当前用户登录 Windows 后重试。",
                ex);
        }

        var data = new ClientConfig(ServerUrl, protectedKey, IsSyncEnabled);
        File.WriteAllText(_configFilePath, JsonSerializer.Serialize(data));
    }

    /// <summary>清空配置，恢复离线模式</summary>
    public void ResetToOffline()
    {
        ServerUrl = null;
        ApiKey = null;
        Username = null;
        IsSyncEnabled = true;
        if (File.Exists(_configFilePath))
        {
            File.Delete(_configFilePath);
        }
    }

    private void LoadConfig()
    {
        if (!File.Exists(_configFilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_configFilePath);
            var data = JsonSerializer.Deserialize<ClientConfig>(json);
            if (data == null)
            {
                return;
            }

            ServerUrl = data.ServerUrl;
            ApiKey = UnprotectSecret(data.ApiKey);
            IsSyncEnabled = data.SyncEnabled;

            // 明文存量配置：读入后立刻改写为加密形式
            if (!string.IsNullOrEmpty(data.ApiKey)
                && !data.ApiKey.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                try
                {
                    SaveConfig();
                }
                catch (InvalidOperationException ex)
                {
                    LogHelper.Error("明文 API Key 升级加密失败，内存中保留密钥但未回写磁盘", ex);
                }
            }
        }
        catch
        {
            /* 配置文件损坏，忽略 */
        }
    }

    /// <summary>DPAPI 加密密钥；失败抛出，禁止明文落盘</summary>
    private static string? ProtectSecret(string? plain)
    {
        if (string.IsNullOrEmpty(plain))
        {
            return plain;
        }

        var bytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain),
            optionalEntropy: null,
            scope: DataProtectionScope.CurrentUser);
        return DpapiPrefix + Convert.ToBase64String(bytes);
    }

    /// <summary>解密 DPAPI 密文；非前缀视为遗留明文</summary>
    private static string? UnprotectSecret(string? stored)
    {
        if (string.IsNullOrEmpty(stored)
            || !stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
        {
            return stored;
        }

        try
        {
            var payload = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
            var bytes = ProtectedData.Unprotect(payload, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            LogHelper.Error("API Key DPAPI 解密失败", ex);
            return null;
        }
    }

    private record VerifyKeyResult(bool valid, string? userId, string? username, string? deviceName);

    private record ClientConfig(string? ServerUrl, string? ApiKey, bool SyncEnabled = true);

    /// <summary>连接探测结果（不写入 AuthService）</summary>
    public readonly record struct ConnectionProbeResult(bool Success, string? Username)
    {
        public static ConnectionProbeResult Failed => new(false, null);
    }
}
