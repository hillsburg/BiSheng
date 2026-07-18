using BiSheng.Server.Data;
using BiSheng.Server.Services.Images;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Services;

/// <summary>
/// 图片垃圾回收后台服务：每小时扫描一次，
/// 硬删过期软删除图片，并软删超期无笔记引用的孤儿图片
/// </summary>
public class ImageGarbageCollector : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ImageGarbageCollector> _logger;

    /// <summary>扫描间隔：每小时</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(1);

    /// <summary>构造图片 GC 后台服务</summary>
    public ImageGarbageCollector(
        IServiceProvider services,
        ILogger<ImageGarbageCollector> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>周期执行图片清理</summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("图片 GC 服务已启动，扫描间隔: {Interval}", ScanInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var cleanup = scope.ServiceProvider.GetRequiredService<ImageCleanupService>();
                var options = scope.ServiceProvider.GetRequiredService<IOptions<ImageCleanupOptions>>().Value;
                var result = await cleanup.RunAsync(stoppingToken);

                if (result.ExpiredRecordsRemoved > 0
                    || result.OrphansSoftDeleted > 0)
                {
                    _logger.LogInformation(
                        "图片 GC 本轮完成: 硬删记录 {Expired}, 硬删文件 {Files}, 孤儿软删 {Orphans} (dry-run={DryRun})",
                        result.ExpiredRecordsRemoved,
                        result.ExpiredFilesDeleted,
                        result.OrphansSoftDeleted,
                        options.OrphanDryRun);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "图片 GC 执行异常");
            }

            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
