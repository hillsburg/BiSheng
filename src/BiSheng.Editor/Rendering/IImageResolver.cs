namespace BiSheng.Editor.Rendering;

/// <summary>
/// 图片解析器接口：由宿主应用实现，将设备无关的 URI（如 bisheng://img/{uuid}）
/// 解析为本地文件路径，供 ImageRenderer 加载渲染。
/// 
/// 宿主应用（如 BiSheng.Latte）负责具体的下载、缓存、文件管理逻辑。
/// </summary>
public interface IImageResolver
{
    /// <summary>
    /// 尝试将 URI 解析为本地文件路径
    /// 返回 null 表示文件尚未就绪（需要下载）
    /// </summary>
    string? ResolveLocalPath(string uri);

    /// <summary>
    /// 获取 URI 对应的解析状态
    /// </summary>
    ImageResolveStatus GetStatus(string uri);

    /// <summary>
    /// 请求后台下载指定 URI 对应的图片
    /// 下载完成后应通知 ImageRenderer 刷新渲染
    /// </summary>
    void RequestDownload(string uri);
}

/// <summary>
/// 图片解析状态
/// </summary>
public enum ImageResolveStatus
{
    /// <summary>本地文件已就绪，可直接加载</summary>
    Ready,

    /// <summary>正在后台下载中</summary>
    Loading,

    /// <summary>文件不可用（未同步 / 下载失败 / 离线）</summary>
    Unavailable
}
