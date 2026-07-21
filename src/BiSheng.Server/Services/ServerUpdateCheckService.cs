using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Services;

/// <summary>
/// 检查服务端更新（只读）：优先拉取 Update:ManifestUrl 清单；
/// 可选回退到 GitHub Releases API。
/// </summary>
public sealed class ServerUpdateCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly ServerUpdateOptions _options;
    private readonly ILogger<ServerUpdateCheckService> _logger;

    /// <summary>构造更新检查服务</summary>
    public ServerUpdateCheckService(
        HttpClient http,
        IOptions<ServerUpdateOptions> options,
        ILogger<ServerUpdateCheckService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>当前程序集版本（InformationalVersion，去掉 +commit）</summary>
    public static string GetCurrentVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    /// <summary>查询最新版本并与当前比较</summary>
    public async Task<ServerUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = GetCurrentVersion();
        if (!_options.Enabled)
        {
            return ServerUpdateCheckResult.Disabled(current);
        }

        var hasManifest = !string.IsNullOrWhiteSpace(_options.ManifestUrl);
        var canGitHub = _options.AllowGitHubFallback
            && !string.IsNullOrWhiteSpace(_options.GitHubOwner)
            && !string.IsNullOrWhiteSpace(_options.GitHubRepo);

        if (!hasManifest && !canGitHub)
        {
            return ServerUpdateCheckResult.Failed(
                current,
                "未配置更新源：请设置 Update:ManifestUrl，或启用 AllowGitHubFallback 并配置 GitHub 仓库。");
        }

        if (hasManifest)
        {
            var fromManifest = await CheckFromManifestAsync(current, cancellationToken).ConfigureAwait(false);
            if (fromManifest.Availability != ServerUpdateAvailability.Failed)
            {
                return fromManifest;
            }

            if (!canGitHub)
            {
                return fromManifest;
            }

            _logger.LogWarning("清单检查失败，回退 GitHub: {Message}", fromManifest.Message);
        }

        return await CheckFromGitHubAsync(current, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>从清单 URL 解析 server 段</summary>
    internal async Task<ServerUpdateCheckResult> CheckFromManifestAsync(
        string current,
        CancellationToken cancellationToken)
    {
        var url = _options.ManifestUrl!.Trim();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("BiSheng.Server-UpdateCheck");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return ServerUpdateCheckResult.Failed(
                    current,
                    $"清单返回 {(int)response.StatusCode}，请检查 Update:ManifestUrl。");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifestDto>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);

            var server = manifest?.Server;
            if (server == null || string.IsNullOrWhiteSpace(server.Version))
            {
                return ServerUpdateCheckResult.Failed(current, "清单缺少 server.version 字段。");
            }

            var expectedRid = string.IsNullOrWhiteSpace(_options.ServerRuntime)
                ? "linux-x64"
                : _options.ServerRuntime.Trim();
            if (!string.IsNullOrWhiteSpace(server.Rid)
                && !string.Equals(server.Rid, expectedRid, StringComparison.OrdinalIgnoreCase))
            {
                return ServerUpdateCheckResult.Failed(
                    current,
                    $"清单 rid={server.Rid} 与配置 ServerRuntime={expectedRid} 不一致。");
            }

            return BuildComparisonResult(
                current,
                NormalizeVersion(server.Version),
                releaseUrl: server.ReleaseNotesUrl,
                downloadUrl: server.DownloadUrl,
                assetName: server.PackageFile,
                sourceHint: "清单");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取更新清单失败: {Url}", url);
            return ServerUpdateCheckResult.Failed(current, $"读取清单失败：{ex.Message}");
        }
    }

    /// <summary>GitHub Releases 回退路径</summary>
    private async Task<ServerUpdateCheckResult> CheckFromGitHubAsync(
        string current,
        CancellationToken cancellationToken)
    {
        var apiUrl =
            $"https://api.github.com/repos/{_options.GitHubOwner}/{_options.GitHubRepo}/releases/latest";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.UserAgent.ParseAdd("BiSheng.Server-UpdateCheck");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return ServerUpdateCheckResult.Failed(current, "仓库尚无 Release。");
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "检查更新失败: HTTP {Status} {Body}",
                    (int)response.StatusCode,
                    body.Length > 200 ? body[..200] : body);
                return ServerUpdateCheckResult.Failed(
                    current,
                    $"GitHub 返回 {(int)response.StatusCode}，请稍后重试或改用 ManifestUrl。");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(
                stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
            {
                return ServerUpdateCheckResult.Failed(current, "无法解析 Release 信息。");
            }

            var latest = NormalizeVersion(release.TagName);
            var runtime = string.IsNullOrWhiteSpace(_options.ServerRuntime)
                ? "linux-x64"
                : _options.ServerRuntime.Trim();
            var assetPattern = new Regex(
                $@"^BiSheng\.Server-.+-{Regex.Escape(runtime)}\.zip$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            var asset = release.Assets?
                .FirstOrDefault(a => a.Name != null && assetPattern.IsMatch(a.Name));

            var releaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl)
                ? $"https://github.com/{_options.GitHubOwner}/{_options.GitHubRepo}/releases/tag/{release.TagName}"
                : release.HtmlUrl;

            return BuildComparisonResult(
                current,
                latest,
                releaseUrl,
                asset?.BrowserDownloadUrl,
                asset?.Name,
                sourceHint: "GitHub",
                missingAssetMessage: $"发现新版本 {latest}，但未找到 {runtime} 安装包，请打开 Release 页手动下载。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查服务端更新异常（GitHub）");
            return ServerUpdateCheckResult.Failed(current, $"检查失败：{ex.Message}");
        }
    }

    private static ServerUpdateCheckResult BuildComparisonResult(
        string current,
        string latest,
        string? releaseUrl,
        string? downloadUrl,
        string? assetName,
        string sourceHint,
        string? missingAssetMessage = null)
    {
        var comparison = CompareVersions(current, latest);
        if (comparison >= 0)
        {
            return new ServerUpdateCheckResult
            {
                Availability = ServerUpdateAvailability.UpToDate,
                CurrentVersion = current,
                LatestVersion = latest,
                ReleaseUrl = releaseUrl,
                DownloadUrl = downloadUrl,
                AssetName = assetName,
                Message = $"已是最新（{current}，来源：{sourceHint}）。"
            };
        }

        var message = string.IsNullOrEmpty(downloadUrl) && missingAssetMessage != null
            ? missingAssetMessage
            : $"发现新版本 {latest}（来源：{sourceHint}）。请下载后用 upgrade-bisheng.sh 升级（管理页不会自动安装）。";

        return new ServerUpdateCheckResult
        {
            Availability = ServerUpdateAvailability.UpdateAvailable,
            CurrentVersion = current,
            LatestVersion = latest,
            ReleaseUrl = releaseUrl,
            DownloadUrl = downloadUrl,
            AssetName = assetName,
            Message = message
        };
    }

    /// <summary>去掉 tag 前缀 v/V</summary>
    public static string NormalizeVersion(string tagOrVersion)
    {
        var s = tagOrVersion.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
        {
            s = s[1..];
        }

        return s;
    }

    /// <summary>比较语义化版本；无法解析时按字符串序。返回 &gt;0 当前更新，0 相等，&lt;0 有新版</summary>
    public static int CompareVersions(string current, string latest)
    {
        if (Version.TryParse(PadVersion(current), out var c)
            && Version.TryParse(PadVersion(latest), out var l))
        {
            return c.CompareTo(l);
        }

        return string.Compare(
            NormalizeVersion(current),
            NormalizeVersion(latest),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string PadVersion(string version)
    {
        var parts = NormalizeVersion(version).Split('.', StringSplitOptions.RemoveEmptyEntries);
        while (parts.Length < 3)
        {
            Array.Resize(ref parts, parts.Length + 1);
            parts[^1] = "0";
        }

        for (var i = 0; i < parts.Length && i < 4; i++)
        {
            var dash = parts[i].IndexOf('-');
            if (dash > 0)
            {
                parts[i] = parts[i][..dash];
            }
        }

        return string.Join('.', parts.Take(4));
    }

    /// <summary>与设计文档 §2.3 对齐的精简清单（仅消费 server 段）</summary>
    internal sealed class UpdateManifestDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("server")]
        public UpdateManifestServerDto? Server { get; set; }
    }

    internal sealed class UpdateManifestServerDto
    {
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("rid")]
        public string? Rid { get; set; }

        [JsonPropertyName("packageFile")]
        public string? PackageFile { get; set; }

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }

        [JsonPropertyName("releaseNotesUrl")]
        public string? ReleaseNotesUrl { get; set; }
    }

    private sealed class GitHubReleaseDto
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubAssetDto>? Assets { get; set; }
    }

    private sealed class GitHubAssetDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}

/// <summary>更新检查结果状态</summary>
public enum ServerUpdateAvailability
{
    /// <summary>配置关闭</summary>
    Disabled,

    /// <summary>已是最新</summary>
    UpToDate,

    /// <summary>有新版本</summary>
    UpdateAvailable,

    /// <summary>检查失败</summary>
    Failed
}

/// <summary>服务端更新检查结果（只读展示）</summary>
public sealed class ServerUpdateCheckResult
{
    /// <summary>状态</summary>
    public ServerUpdateAvailability Availability { get; init; }

    /// <summary>当前版本</summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>最新版本（若可知）</summary>
    public string? LatestVersion { get; init; }

    /// <summary>Release 页面</summary>
    public string? ReleaseUrl { get; init; }

    /// <summary>zip 直链（若匹配到资产）</summary>
    public string? DownloadUrl { get; init; }

    /// <summary>资产文件名</summary>
    public string? AssetName { get; init; }

    /// <summary>展示文案</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>配置关闭</summary>
    public static ServerUpdateCheckResult Disabled(string current) => new()
    {
        Availability = ServerUpdateAvailability.Disabled,
        CurrentVersion = current,
        Message = "更新检查已在配置中关闭（Update:Enabled=false）。"
    };

    /// <summary>失败</summary>
    public static ServerUpdateCheckResult Failed(string current, string message) => new()
    {
        Availability = ServerUpdateAvailability.Failed,
        CurrentVersion = current,
        Message = message
    };
}
