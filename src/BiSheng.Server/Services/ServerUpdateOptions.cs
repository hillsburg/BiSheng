namespace BiSheng.Server.Services;

/// <summary>管理页「检查服务端更新」配置（只读展示，不自动升级）</summary>
public sealed class ServerUpdateOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "Update";

    /// <summary>是否启用检查（关闭后管理页提示已禁用）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 更新清单 URL（优先）。指向稳定托管的 update-manifest.json，
    /// 可放在任意 HTTPS 静态站 / 对象存储，不依赖 GitHub API。
    /// </summary>
    public string? ManifestUrl { get; set; }

    /// <summary>
    /// 清单不可用时是否回退到 GitHub Releases API。
    /// 无外网或希望完全解耦时可设为 false。
    /// </summary>
    public bool AllowGitHubFallback { get; set; } = true;

    /// <summary>GitHub 仓库 Owner（仅回退路径使用）</summary>
    public string GitHubOwner { get; set; } = "hillsburg";

    /// <summary>GitHub 仓库名（仅回退路径使用）</summary>
    public string GitHubRepo { get; set; } = "BiSheng";

    /// <summary>匹配的 Server 发布包 Runtime / rid（资产名或清单字段）</summary>
    public string ServerRuntime { get; set; } = "linux-x64";
}
