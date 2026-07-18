using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BiSheng.Server.Services.Images;

/// <summary>
/// 图片清理核心逻辑：过期软删除硬清 + 无引用孤儿软删
/// </summary>
public class ImageCleanupService
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ImageCleanupOptions _options;
    private readonly ILogger<ImageCleanupService> _logger;

    /// <summary>磁盘上传根目录名</summary>
    private static readonly string UploadDir = "uploads";

    /// <summary>构造图片清理服务</summary>
    public ImageCleanupService(
        AppDbContext db,
        IWebHostEnvironment env,
        IOptions<ImageCleanupOptions> options,
        ILogger<ImageCleanupService> logger)
    {
        _db = db;
        _env = env;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// 执行一轮清理：先硬删过期软删除，再扫描并软删孤儿
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>硬删条数、磁盘文件数、孤儿软删（或 dry-run 计数）</returns>
    public async Task<ImageCleanupResult> RunAsync(CancellationToken ct = default)
    {
        var expired = await CleanupExpiredSoftDeletesAsync(ct);
        var orphans = await SoftDeleteOrphansAsync(ct);
        return new ImageCleanupResult(expired.Records, expired.Files, orphans);
    }

    /// <summary>删除已软删除且超过 ImageRetentionDays 的图片记录与磁盘文件</summary>
    private async Task<(int Records, int Files)> CleanupExpiredSoftDeletesAsync(CancellationToken ct)
    {
        var config = await _db.ServerConfigs.FirstOrDefaultAsync(ct) ?? new ServerConfig();
        var cutoff = DateTime.UtcNow.AddDays(-config.ImageRetentionDays);

        var expired = await _db.Images
            .Where(i => i.IsDeleted && i.DeletedAt != null && i.DeletedAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0)
        {
            return (0, 0);
        }

        var deletedFiles = 0;
        foreach (var image in expired)
        {
            var filePath = GetDiskPath(image);
            if (File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                    deletedFiles++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "无法删除图片文件: {Path}", filePath);
                }
            }

            _db.Images.Remove(image);
        }

        await _db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "图片 GC 硬删完成: 清理 {Count} 条记录, 删除 {Files} 个文件",
            expired.Count,
            deletedFiles);

        return (expired.Count, deletedFiles);
    }

    /// <summary>
    /// 扫描笔记引用：超期且无引用的未删除图片标记为软删除（或 dry-run 仅记日志）
    /// </summary>
    private async Task<int> SoftDeleteOrphansAsync(CancellationToken ct)
    {
        var graceDays = Math.Max(1, _options.OrphanGraceDays);
        var cutoff = DateTime.UtcNow.AddDays(-graceDays);

        // 按用户分批：先取候选孤儿，再对该用户扫笔记正文
        var candidates = await _db.Images
            .Where(i => !i.IsDeleted && i.CreatedAt < cutoff)
            .Select(i => new { i.Id, i.UserId, i.CreatedAt })
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var orphanCount = 0;
        var now = DateTime.UtcNow;

        foreach (var group in candidates.GroupBy(c => c.UserId))
        {
            var userId = group.Key;
            var candidateIds = group.Select(c => c.Id).ToHashSet();

            var noteContents = await _db.Notes
                .Where(n => n.UserId == userId)
                .Select(n => n.Content)
                .ToListAsync(ct);

            var referenced = NoteImageReferenceScanner.ExtractImageIds(noteContents);
            var orphanIds = candidateIds.Where(id => !referenced.Contains(id)).ToList();
            if (orphanIds.Count == 0)
            {
                continue;
            }

            if (_options.OrphanDryRun)
            {
                orphanCount += orphanIds.Count;
                _logger.LogInformation(
                    "图片孤儿 GC dry-run: 用户 {UserId} 将软删 {Count} 张无引用图片",
                    userId,
                    orphanIds.Count);
                continue;
            }

            var orphans = await _db.Images
                .Where(i => i.UserId == userId && orphanIds.Contains(i.Id) && !i.IsDeleted)
                .ToListAsync(ct);

            foreach (var image in orphans)
            {
                image.IsDeleted = true;
                image.DeletedAt = now;
            }

            if (orphans.Count > 0)
            {
                await _db.SaveChangesAsync(ct);
                orphanCount += orphans.Count;
                _logger.LogInformation(
                    "图片孤儿 GC: 用户 {UserId} 软删 {Count} 张无引用图片",
                    userId,
                    orphans.Count);
            }
        }

        return orphanCount;
    }

    /// <summary>拼接 uploads/{userId}/{id}{ext} 磁盘路径</summary>
    private string GetDiskPath(ServerImage image)
    {
        return Path.Combine(
            _env.ContentRootPath,
            UploadDir,
            image.UserId.ToString(),
            $"{image.Id}{image.Extension}");
    }
}

/// <summary>一轮图片清理结果</summary>
/// <param name="ExpiredRecordsRemoved">硬删的软删除记录数</param>
/// <param name="ExpiredFilesDeleted">硬删的磁盘文件数</param>
/// <param name="OrphansSoftDeleted">孤儿软删数（dry-run 时为将删计数）</param>
public record ImageCleanupResult(
    int ExpiredRecordsRemoved,
    int ExpiredFilesDeleted,
    int OrphansSoftDeleted);
