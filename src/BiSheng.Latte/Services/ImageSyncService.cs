using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using BiSheng.Latte.Models;
using BiSheng.Shared.Sync;

namespace BiSheng.Latte.Services;

/// <summary>
/// 图片同步服务：独立于笔记文本同步管道，负责图片的上传和下载
/// 
/// 与 SyncService（笔记同步）完全独立：
/// - 笔记同步：Note.Content (含 bisheng://img/uuid) → SyncController → 文本增量同步
/// - 图片同步：images/{uuid}.png → ImageSyncService → ImagesController → 逐张上传/下载
/// 
/// 功能：
/// 1. 上传队列：后台定时扫描 LocalImage 表中 Synced=false 的记录，逐张上传
/// 2. 下载：按需从服务器下载图片到本地
/// 3. 增量拉取：定时检查服务端新增的图片，自动下载到本地
/// </summary>
public class ImageSyncService : IDisposable
{
    private readonly AuthService _authService;
    private readonly ApiClient _apiClient;
    private readonly ImageStorageService _imageStorage;
    private readonly Func<LocalDbContext> _dbFactory;
    private SyncSettings _settings;

    /// <summary>上传轮询定时器</summary>
    private System.Timers.Timer? _uploadTimer;

    /// <summary>增量拉取定时器</summary>
    private System.Timers.Timer? _pullTimer;

    /// <summary>防止并发上传</summary>
    private readonly SemaphoreSlim _uploadLock = new(1, 1);

    /// <summary>服务是否已进入最终释放阶段</summary>
    private int _disposeState;

    /// <summary>正在下载的图片 ID 集合（防止重复下载）</summary>
    private readonly HashSet<string> _downloadingIds = new();
    private readonly object _downloadingLock = new();

    /// <summary>图片本地存储目录</summary>
    private static string ImagesDirectory => LatteAppPaths.ImagesDirectory;

    /// <summary>
    /// 图片下载完成事件：参数为 (imageId, localPath)
    /// UI 层订阅此事件来触发编辑器重绘
    /// </summary>
    public event Action<Guid, string>? OnImageDownloaded;

    /// <summary>
    /// 图片同步状态变更事件：参数为 (synced, total, message)
    /// </summary>
    public event Action<int, int, string>? OnSyncProgressChanged;

    public ImageSyncService(
        AuthService authService,
        ApiClient apiClient,
        ImageStorageService imageStorage,
        Func<LocalDbContext> dbFactory,
        SyncSettings settings)
    {
        _authService = authService;
        _apiClient = apiClient;
        _imageStorage = imageStorage;
        _dbFactory = dbFactory;
        _settings = settings;

        _uploadTimer = new System.Timers.Timer(_settings.ImageUploadIntervalMs) { AutoReset = true };
        _uploadTimer.Elapsed += (_, _) => _ = UploadUnsyncedImagesAsync();

        _pullTimer = new System.Timers.Timer(_settings.ImagePullIntervalMs) { AutoReset = true };
        _pullTimer.Elapsed += (_, _) => _ = PullNewImagesAsync();
    }

    /// <summary>应用新的同步配置（保存设置后更新图片轮询间隔）</summary>
    public void ApplySettings(SyncSettings settings)
    {
        ThrowIfDisposed();
        _settings = settings;
        if (_uploadTimer != null)
        {
            _uploadTimer.Interval = settings.ImageUploadIntervalMs;
        }

        if (_pullTimer != null)
        {
            _pullTimer.Interval = settings.ImagePullIntervalMs;
        }
    }

    // ========================================================
    //  公共方法
    // ========================================================

    /// <summary>
    /// 启动图片同步服务
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();

        if (!_authService.IsConnected)
        {
            LogHelper.Warn("图片同步服务启动失败：未连接服务器");
            return;
        }

        _uploadTimer?.Start();
        _pullTimer?.Start();

        // 启动后立即执行一次上传
        _ = UploadUnsyncedImagesAsync();
        LogHelper.Info("图片同步服务已启动");
    }

    /// <summary>
    /// 停止图片同步定时器
    /// </summary>
    public void Stop()
    {
        _uploadTimer?.Stop();
        _pullTimer?.Stop();
    }

    /// <summary>
    /// 退出/停机前尽快上传尚未同步的本地图片（与笔记 FlushOnExit 对齐）。
    /// 会等待进行中的上传结束，避免忙时 WaitAsync(0) 空跑。
    /// </summary>
    public async Task FlushPendingUploadsAsync()
    {
        if (Volatile.Read(ref _disposeState) != 0 || !_authService.IsConnected)
        {
            return;
        }

        await UploadUnsyncedImagesAsync(waitIfBusy: true);
    }

    /// <summary>
    /// 立即下载指定图片（由 IImageResolver.RequestDownload 触发）
    /// </summary>
    public async Task DownloadImageAsync(string imageIdStr)
    {
        if (!Guid.TryParse(imageIdStr, out var imageId)) return;
        await DownloadImageByIdAsync(imageId);
    }

    /// <summary>
    /// 检查本地是否已缓存指定图片
    /// </summary>
    public bool IsImageAvailableLocally(string imageIdStr)
    {
        var localPath = GetLocalImagePath(imageIdStr);
        return localPath != null && File.Exists(localPath);
    }

    /// <summary>
    /// 网络恢复时立即上传并拉取图片（SignalR 重连等场景调用）
    /// </summary>
    public Task OnNetworkRecoveredAsync()
    {
        if (Volatile.Read(ref _disposeState) != 0 || !_authService.IsConnected)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(UploadUnsyncedImagesAsync(), PullNewImagesAsync());
    }

    /// <summary>
    /// 获取图片的本地文件路径（不存在返回 null）
    /// </summary>
    public string? GetLocalImagePath(string imageIdStr)
    {
        if (!Directory.Exists(ImagesDirectory))
        {
            return null;
        }

        var pngPath = Path.Combine(ImagesDirectory, $"{imageIdStr}.png");
        if (File.Exists(pngPath))
        {
            return pngPath;
        }

        // 跨设备拉取可能保存为非 .png 扩展名
        var matches = Directory.EnumerateFiles(ImagesDirectory, $"{imageIdStr}.*").ToList();
        return matches.Count > 0 ? matches[0] : null;
    }

    /// <summary>
    /// 检查某图片是否正在下载中
    /// </summary>
    public bool IsDownloading(string imageIdStr)
    {
        lock (_downloadingLock)
        {
            return _downloadingIds.Contains(imageIdStr);
        }
    }

    // ========================================================
    //  上传
    // ========================================================

    /// <summary>退出 flush 等待上传锁的最长时间</summary>
    private static readonly TimeSpan ShutdownUploadWait = TimeSpan.FromSeconds(90);

    /// <summary>
    /// 扫描并上传所有未同步的图片
    /// </summary>
    /// <param name="waitIfBusy">为 true 时等待进行中的上传结束（退出 flush）</param>
    private async Task UploadUnsyncedImagesAsync(bool waitIfBusy = false)
    {
        if (Volatile.Read(ref _disposeState) != 0 || !_authService.IsConnected)
        {
            return;
        }

        var lockWait = waitIfBusy ? ShutdownUploadWait : TimeSpan.Zero;
        if (!await _uploadLock.WaitAsync(lockWait))
        {
            if (waitIfBusy)
            {
                LogHelper.Warn("退出图片上传等待超时，仍可能有未上传图片");
            }

            return;
        }

        try
        {
            var unsynced = _imageStorage.GetUnsyncedImages();
            if (unsynced.Count == 0) return;

            int synced = 0;
            int total = unsynced.Count;
            LogHelper.Info("开始上传 {0} 张未同步图片", total);

            foreach (var image in unsynced)
            {
                if (!_authService.IsConnected) break;

                try
                {
                    // 本地文件缺失不得标为已同步，否则其他设备永远拉不到
                    if (string.IsNullOrWhiteSpace(image.FilePath) || !File.Exists(image.FilePath))
                    {
                        _imageStorage.MarkFailed(image.Id);
                        LogHelper.Warn("图片本地文件缺失，标记失败待重试: {0}", image.Id);
                        continue;
                    }

                    await UploadSingleImageAsync(image);
                    _imageStorage.MarkSynced(image.Id);
                    synced++;
                    OnSyncProgressChanged?.Invoke(synced, total, $"上传图片 {synced}/{total}");
                }
                catch (Exception ex)
                {
                    _imageStorage.MarkFailed(image.Id);
                    LogHelper.Error($"图片上传失败: {image.Id}", ex);
                }
            }

            if (synced > 0)
            {
                OnSyncProgressChanged?.Invoke(synced, total, $"已上传 {synced} 张图片");
                LogHelper.Info("图片上传完成: {0}/{1}", synced, total);
            }
        }
        finally
        {
            _uploadLock.Release();
        }
    }

    /// <summary>
    /// 上传单张图片到服务器（调用方已确认本地文件存在）
    /// </summary>
    private async Task UploadSingleImageAsync(LocalImage image)
    {
        var fileBytes = await File.ReadAllBytesAsync(image.FilePath);
        var fileName = $"{image.Id}{Path.GetExtension(image.FilePath)}";

        await _apiClient.PostUploadAsync($"/api/images/{image.Id}", fileBytes, fileName);
    }

    // ========================================================
    //  下载
    // ========================================================

    /// <summary>
    /// 下载指定图片到本地
    /// </summary>
    /// <returns>是否已在本地可用（原本就有或本次下载成功）</returns>
    private async Task<bool> DownloadImageByIdAsync(Guid imageId, string extension = ".png")
    {
        var idStr = imageId.ToString();
        var ext = string.IsNullOrWhiteSpace(extension) ? ".png" : extension;
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        // 防止重复下载
        lock (_downloadingLock)
        {
            if (_downloadingIds.Contains(idStr))
            {
                return File.Exists(Path.Combine(ImagesDirectory, $"{idStr}{ext}"));
            }

            if (!_authService.IsConnected)
            {
                return false;
            }

            _downloadingIds.Add(idStr);
        }

        try
        {
            // 检查本地是否已存在
            var localPath = Path.Combine(ImagesDirectory, $"{idStr}{ext}");
            if (File.Exists(localPath))
            {
                OnImageDownloaded?.Invoke(imageId, localPath);
                return true;
            }

            // 从服务器下载
            var response = await _apiClient.GetBytesAsync($"/api/images/{imageId}");
            if (response == null || response.Length == 0)
            {
                return false;
            }

            // 确保目录存在
            if (!Directory.Exists(ImagesDirectory))
            {
                Directory.CreateDirectory(ImagesDirectory);
            }

            await File.WriteAllBytesAsync(localPath, response);

            OnImageDownloaded?.Invoke(imageId, localPath);
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Warn("图片下载失败: {0}", ex.Message);
            return false;
        }
        finally
        {
            lock (_downloadingLock)
            {
                _downloadingIds.Remove(idStr);
            }
        }
    }

    // ========================================================
    //  增量拉取
    // ========================================================

    /// <summary>
    /// 增量拉取：查询服务端自上次同步后新增的图片，下载到本地
    /// </summary>
    private async Task PullNewImagesAsync()
    {
        if (Volatile.Read(ref _disposeState) != 0 || !_authService.IsConnected) return;

        try
        {
            // 查询上次拉取时间
            using var db = _dbFactory();
            var state = db.SyncState.FirstOrDefault();
            var since = state?.LastImagePullTime ?? DateTime.UtcNow.AddDays(-1);

            // 服务端返回 { images, serverTime } 包装对象
            var response = await _apiClient.GetAsync<ImagePendingResponse>(
                $"/api/images/pending?since={since:O}");

            var pending = response?.Images;
            if (pending == null || pending.Count == 0)
            {
                // 空列表可推进游标（使用服务端时间，避免时钟偏差）
                UpdateImagePullTime(db, response?.ServerTime);
                return;
            }

            var allOk = true;
            foreach (var item in pending)
            {
                if (item.IsDeleted)
                {
                    continue;
                }

                var ext = string.IsNullOrWhiteSpace(item.Extension) ? ".png" : item.Extension;
                var localPath = Path.Combine(ImagesDirectory, $"{item.Id}{ext}");
                if (File.Exists(localPath))
                {
                    continue;
                }

                if (!await DownloadImageByIdAsync(item.Id, ext))
                {
                    allOk = false;
                    LogHelper.Warn("图片增量拉取未完成: {0}", item.Id);
                }
            }

            // 任一下载失败则不推进游标，下次仍会重试
            if (allOk)
            {
                UpdateImagePullTime(db, response.ServerTime);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Warn("图片增量拉取失败: {0}", ex.Message);
        }
    }

    /// <summary>
    /// 更新最后图片拉取时间（优先使用服务端时间）
    /// </summary>
    private static void UpdateImagePullTime(LocalDbContext db, DateTime? serverTime = null)
    {
        var state = db.SyncState.FirstOrDefault();
        if (state == null)
        {
            return;
        }

        state.LastImagePullTime = serverTime ?? DateTime.UtcNow;
        db.SyncState.Update(state);
        db.SaveChangesWithLock();
    }

    // ========================================================
    //  资源释放
    // ========================================================

    /// <summary>确认图片同步服务尚未进入最终释放阶段</summary>
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(nameof(ImageSyncService));
        }
    }

    /// <summary>停止定时任务并释放图片同步资源</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _uploadTimer?.Stop();
        _pullTimer?.Stop();
        _uploadTimer?.Dispose();
        _pullTimer?.Dispose();
        _uploadTimer = null;
        _pullTimer = null;

        // 最多等待正在执行的上传结束，避免释放仍在使用的并发锁。
        if (_uploadLock.Wait(5000))
        {
            _uploadLock.Dispose();
        }
    }
}
