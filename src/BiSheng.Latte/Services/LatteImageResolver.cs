using System.IO;
using BiSheng.Editor.Rendering;

namespace BiSheng.Latte.Services;

/// <summary>
/// 图片解析器桥接：实现 IImageResolver 接口，将编辑器的 bisheng://img/{uuid} URI
/// 解析为本地文件路径，并通过 ImageSyncService 触发后台下载。
/// 
/// 解析流程：
/// 1. 检查 images/{uuid}.png 是否存在 → 存在则返回路径（Ready）
/// 2. 不存在 → 检查是否正在下载中（Loading）
/// 3. 未下载 → 触发 ImageSyncService.DownloadImageAsync（Unavailable）
/// 
/// 下载完成后通过 OnImageResolved 事件通知 UI 层触发编辑器重绘。
/// </summary>
public class LatteImageResolver : IImageResolver
{
    private readonly ImageSyncService _imageSync;

    /// <summary>
    /// 图片解析完成事件：参数为 bisheng:// URI
    /// UI 层订阅此事件来触发编辑器重绘
    /// </summary>
    public event Action<string>? OnImageResolved;

    public LatteImageResolver(ImageSyncService imageSync)
    {
        _imageSync = imageSync;

        // 监听图片下载完成 → 通知 UI 重绘
        _imageSync.OnImageDownloaded += (imageId, localPath) =>
        {
            var uri = $"bisheng://img/{imageId}";
            OnImageResolved?.Invoke(uri);
        };
    }

    /// <summary>
    /// 将 bisheng://img/{uuid} URI 解析为本地文件路径
    /// 返回 null 表示文件尚未就绪（需要下载）
    /// </summary>
    public string? ResolveLocalPath(string uri)
    {
        var imageId = ExtractImageId(uri);
        if (imageId == null) return null;

        return _imageSync.GetLocalImagePath(imageId);
    }

    /// <summary>
    /// 获取 URI 对应的解析状态
    /// </summary>
    public ImageResolveStatus GetStatus(string uri)
    {
        var imageId = ExtractImageId(uri);
        if (imageId == null) return ImageResolveStatus.Unavailable;

        // 本地有文件 → Ready
        if (_imageSync.IsImageAvailableLocally(imageId))
            return ImageResolveStatus.Ready;

        // 正在下载中 → Loading
        if (_imageSync.IsDownloading(imageId))
            return ImageResolveStatus.Loading;

        // 未下载 → Unavailable
        return ImageResolveStatus.Unavailable;
    }

    /// <summary>
    /// 请求后台下载指定 URI 对应的图片
    /// </summary>
    public void RequestDownload(string uri)
    {
        var imageId = ExtractImageId(uri);
        if (imageId == null) return;

        // 已经在下载中则跳过
        if (_imageSync.IsDownloading(imageId)) return;

        // 异步触发下载（不阻塞 UI）
        _ = _imageSync.DownloadImageAsync(imageId);
    }

    /// <summary>
    /// 从 bisheng://img/{uuid} 中提取 UUID 字符串
    /// </summary>
    private static string? ExtractImageId(string uri)
    {
        const string prefix = "bisheng://img/";
        if (!uri.StartsWith(prefix)) return null;
        var id = uri.Substring(prefix.Length);
        return string.IsNullOrWhiteSpace(id) ? null : id;
    }
}
