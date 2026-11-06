namespace BiSheng.Server.Auth;

/// <summary>每个部署独立配置的 WebAuthn 信任边界，禁止从请求 Host 推导。</summary>
public sealed class WebAuthnOptions
{
    /// <summary>浏览器端仪式的超时。</summary>
    public static readonly TimeSpan CeremonyTimeout = TimeSpan.FromMinutes(2);
    /// <summary>服务端挑战比浏览器超时多保留一分钟，覆盖提交时的网络往返。</summary>
    public static readonly TimeSpan ChallengeTimeToLive = CeremonyTimeout + TimeSpan.FromMinutes(1);
    /// <summary>单个账号可绑定的通行密钥上限。</summary>
    public const int MaxCredentialsPerUser = 20;

    public bool Enabled { get; set; }
    public string RpId { get; set; } = string.Empty;
    public string RpName { get; set; } = "Bisheng";
    public string[] Origins { get; set; } = [];

    /// <summary>启用时校验域名及精确来源；只有 localhost 允许 HTTP 开发调试。</summary>
    public bool IsValid()
    {
        if (!Enabled)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(RpName) || Uri.CheckHostName(RpId) != UriHostNameType.Dns
            || RpId.Contains('*') || RpId != RpId.ToLowerInvariant() || Origins.Length == 0)
        {
            return false;
        }
        return Origins.All(origin => Uri.TryCreate(origin, UriKind.Absolute, out var uri)
            && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && origin == uri.GetLeftPart(UriPartial.Authority)
            && (uri.Scheme == "https" || (uri.Scheme == "http" && uri.Host == "localhost" && RpId == "localhost"))
            && (uri.Host == RpId || uri.Host.EndsWith("." + RpId, StringComparison.OrdinalIgnoreCase)));
    }
}
