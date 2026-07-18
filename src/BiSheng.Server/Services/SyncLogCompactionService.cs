using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Services;

/// <summary>
/// SyncLog 安全裁剪后台服务：按活跃客户端最小 LastSyncVersion 删除旧日志
/// </summary>
public class SyncLogCompactionService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<SyncLogCompactionService> _logger;

    public SyncLogCompactionService(
        IServiceProvider services,
        ILogger<SyncLogCompactionService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SyncLog 裁剪服务已启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = TimeSpan.FromHours(24);
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var config = await db.ServerConfigs.FirstOrDefaultAsync(stoppingToken)
                    ?? new ServerConfig();
                interval = TimeSpan.FromHours(Math.Max(1, config.SyncLogCompactionIntervalHours));

                await RunCompactionAsync(scope.ServiceProvider, config, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "SyncLog 裁剪执行异常");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunCompactionAsync(
        IServiceProvider services,
        ServerConfig config,
        CancellationToken ct)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var clientSync = services.GetRequiredService<ClientSyncStateService>();

        var staleMarked = await clientSync.MarkStaleClientsAsync(db, config.SyncLogStaleClientDays, ct);
        if (staleMarked > 0)
            _logger.LogDebug("SyncLog 裁剪: 标记 {Count} 个 stale 客户端", staleMarked);

        var userIds = await db.SyncLogs
            .Select(s => s.UserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var userId in userIds)
        {
            var logCount = await db.SyncLogs.CountAsync(s => s.UserId == userId, ct);
            if (logCount < config.SyncLogMinEntriesForCompaction)
                continue;

            var cutoff = await clientSync.ComputeSafeCutoffAsync(db, userId, ct);
            if (!cutoff.HasValue || cutoff.Value <= 0)
            {
                continue;
            }

            // 删除与 floor 更新必须原子提交，否则落后客户端可能静默丢变更
            var deleted = await SyncLogCompactor.CompactUserAsync(
                db, clientSync, userId, cutoff.Value, ct);

            if (deleted <= 0)
            {
                continue;
            }

            _logger.LogInformation(
                "用户 {UserId} SyncLog 裁剪: 删除 {Deleted} 条, 保留线 Version >= {Cutoff}",
                userId, deleted, cutoff.Value);
        }
    }
}
