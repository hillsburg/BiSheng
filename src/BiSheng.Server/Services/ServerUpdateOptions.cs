namespace BiSheng.Server.Services;

/// <summary>管理页「检查服务端更新」配置（只读展示，不自动升级）</summary>
public sealed class ServerUpdateOptions
{
    /// <summary>配置节名</summary>
    public const string SectionName = "Update";

    /// <summary>是否启用检查（关闭后管理页提示已禁用）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>GitHub 仓库 Owner</summary>
    public string GitHubOwner { get; set; } = "hillsburg";

    /// <summary>GitHub 仓库名</summary>
    public string GitHubRepo { get; set; } = "BiSheng";

    /// <summary>匹配的 Server 发布包 Runtime（资产名中的一段）</summary>
    public string ServerRuntime { get; set; } = "linux-x64";
}
