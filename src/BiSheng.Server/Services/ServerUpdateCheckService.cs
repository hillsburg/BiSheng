using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Services;

/// <summary>检查 GitHub Releases 上是否有更新的服务端包（只读）</summary>
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

    /// <summary>查询最新稳定 Release 并与当前版本比较</summary>
    public async Task<ServerUpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var current = GetCurrentVersion();
        if (!_options.Enabled)
        {
            return ServerUpdateCheckResult.Disabled(current);
        }

        if (string.IsNullOrWhiteSpace(_options.GitHubOwner) || string.IsNullOrWhiteSpace(_options.GitHubRepo))
        {
            return ServerUpdateCheckResult.Failed(current, "未配置 GitHub 仓库。");
        }

        var url =
            $"https://api.github.com/repos/{_options.GitHubOwner}/{_options.GitHubRepo}/releases/latest";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
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
                    $"GitHub 返回 {(int)response.StatusCode}，请稍后重试。");
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

            var comparison = CompareVersions(current, latest);
            if (comparison >= 0)
            {
                return new ServerUpdateCheckResult
                {
                    Availability = ServerUpdateAvailability.UpToDate,
                    CurrentVersion = current,
                    LatestVersion = latest,
                    ReleaseUrl = releaseUrl,
                    DownloadUrl = asset?.BrowserDownloadUrl,
                    AssetName = asset?.Name,
                    Message = $"已是最新（{current}）。"
                };
            }

            return new ServerUpdateCheckResult
            {
                Availability = ServerUpdateAvailability.UpdateAvailable,
                CurrentVersion = current,
                LatestVersion = latest,
                ReleaseUrl = releaseUrl,
                DownloadUrl = asset?.BrowserDownloadUrl,
                AssetName = asset?.Name,
                Message = asset == null
                    ? $"发现新版本 {latest}，但未找到 {runtime} 安装包，请打开 Release 页手动下载。"
                    : $"发现新版本 {latest}。请下载后用 upgrade-bisheng.sh 升级（管理页不会自动安装）。"
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查服务端更新异常");
            return ServerUpdateCheckResult.Failed(current, $"检查失败：{ex.Message}");
        }
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

        // Version 最多 4 段；截断预发布后缀
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
