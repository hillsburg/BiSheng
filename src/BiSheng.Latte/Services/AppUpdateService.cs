using Velopack;
using Velopack.Sources;

namespace BiSheng.Latte.Services;

/// <summary>检查更新结果可用性</summary>
public enum AppUpdateAvailability
{
    /// <summary>当前不是 Velopack 安装版（dotnet run / 绿色解压）</summary>
    NotInstalled,

    /// <summary>已是最新</summary>
    UpToDate,

    /// <summary>发现新版本</summary>
    UpdateAvailable,

    /// <summary>检查失败</summary>
    Failed
}

/// <summary>一次检查更新的结果（含后续下载所需状态）</summary>
public sealed class AppUpdateCheckResult
{
    /// <summary>结果类别</summary>
    public AppUpdateAvailability Availability { get; init; }

    /// <summary>当前版本</summary>
    public string CurrentVersion { get; init; } = string.Empty;

    /// <summary>可用新版本（若有）</summary>
    public string? AvailableVersion { get; init; }

    /// <summary>说明或错误信息</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>Velopack 管理器（仅 UpdateAvailable 时有效）</summary>
    internal UpdateManager? Manager { get; init; }

    /// <summary>更新包信息（仅 UpdateAvailable 时有效）</summary>
    internal UpdateInfo? UpdateInfo { get; init; }
}

/// <summary>
/// Latte 自动更新：对接公开仓 GitHub Releases（Velopack）。
/// 默认不静默安装，由 UI 确认后下载并重启。
/// </summary>
public sealed class AppUpdateService
{
    /// <summary>更新源仓库</summary>
    public const string GitHubRepoUrl = "https://github.com/hillsburg/BiSheng";

    /// <summary>是否为 Velopack 安装版</summary>
    public bool IsInstalled
    {
        get
        {
            try
            {
                return CreateManager().IsInstalled;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>当前 Velopack / 程序集版本</summary>
    public string GetCurrentVersionDisplay()
    {
        try
        {
            var mgr = CreateManager();
            if (mgr.IsInstalled && mgr.CurrentVersion != null)
            {
                return mgr.CurrentVersion.ToString();
            }
        }
        catch
        {
            // 回退程序集版本
        }

        return LatteAppVersion.DisplayVersion;
    }

    /// <summary>检查是否有可用更新</summary>
    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var mgr = CreateManager();
            var current = mgr.CurrentVersion?.ToString() ?? LatteAppVersion.DisplayVersion;

            if (!mgr.IsInstalled)
            {
                return new AppUpdateCheckResult
                {
                    Availability = AppUpdateAvailability.NotInstalled,
                    CurrentVersion = current,
                    Message = "当前为开发或便携运行模式，不支持应用内更新。请使用 GitHub Releases 中的 Setup 安装版。"
                };
            }

            var update = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (update == null)
            {
                return new AppUpdateCheckResult
                {
                    Availability = AppUpdateAvailability.UpToDate,
                    CurrentVersion = current,
                    Message = "已是最新版本。"
                };
            }

            var available = update.TargetFullRelease.Version.ToString();
            return new AppUpdateCheckResult
            {
                Availability = AppUpdateAvailability.UpdateAvailable,
                CurrentVersion = current,
                AvailableVersion = available,
                Message = $"发现新版本 {available}（当前 {current}）。",
                Manager = mgr,
                UpdateInfo = update
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogHelper.Error("检查更新失败", ex);
            return new AppUpdateCheckResult
            {
                Availability = AppUpdateAvailability.Failed,
                CurrentVersion = LatteAppVersion.DisplayVersion,
                Message = $"检查更新失败：{ex.Message}"
            };
        }
    }

    /// <summary>下载并应用更新后重启（须已确认）</summary>
    /// <param name="beforeRestart">下载完成后、进程重启前的回调（释放托盘 / DI 等）</param>
    public async Task DownloadAndApplyAsync(
        AppUpdateCheckResult result,
        IProgress<int>? progress = null,
        Action? beforeRestart = null,
        CancellationToken cancellationToken = default)
    {
        if (result.Availability != AppUpdateAvailability.UpdateAvailable
            || result.Manager == null
            || result.UpdateInfo == null)
        {
            throw new InvalidOperationException("没有可应用的更新。");
        }

        await result.Manager.DownloadUpdatesAsync(
            result.UpdateInfo,
            progress == null ? null : p => progress.Report(p),
            cancelToken: cancellationToken).ConfigureAwait(false);

        beforeRestart?.Invoke();
        result.Manager.ApplyUpdatesAndRestart(result.UpdateInfo);
    }

    /// <summary>创建指向公开仓 Releases 的 UpdateManager</summary>
    private static UpdateManager CreateManager()
    {
        var source = new GithubSource(GitHubRepoUrl, accessToken: null, prerelease: false);
        return new UpdateManager(source);
    }
}
