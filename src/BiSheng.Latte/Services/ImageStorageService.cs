using System.IO;
using BiSheng.Latte.Data;
using BiSheng.Latte.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Latte.Services;

/// <summary>
/// 本地图片存储服务：管理 LocalImage 表的 CRUD 操作
/// 
/// 职责：
/// - 记录粘贴的图片元数据（RecordImage）
/// - 查询未同步的图片（GetUnsyncedImages）
/// - 标记上传成功（MarkSynced）
/// - 标记上传失败（MarkFailed）
/// - 查询某笔记的所有图片（GetImagesForNote）
/// </summary>
public class ImageStorageService
{
    private readonly Func<LocalDbContext> _dbFactory;

    public ImageStorageService(Func<LocalDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// 记录新粘贴的图片到 LocalImage 表，标记 Synced=false
    /// </summary>
    public void RecordImage(Guid imageId, Guid noteId, string filePath)
    {
        using var db = _dbFactory();

        // 幂等：如果已存在则跳过
        if (db.Images.Find(imageId) != null) return;

        var fileInfo = new FileInfo(filePath);
        var image = new LocalImage
        {
            Id = imageId,
            NoteId = noteId,
            FileName = Path.GetFileName(filePath),
            FilePath = filePath,
            FileSize = fileInfo.Exists ? fileInfo.Length : 0,
            ContentType = GuessContentType(filePath),
            Synced = false,
            CreatedAt = DateTime.UtcNow
        };

        db.Images.Add(image);
        db.SaveChangesWithLock();
    }

    /// <summary>
    /// 获取所有未同步的图片（Synced=false 且重试次数未超限）
    /// </summary>
    public List<LocalImage> GetUnsyncedImages(int maxRetryCount = 3)
    {
        using var db = _dbFactory();
        return db.Images
            .Where(i => !i.Synced && i.RetryCount < maxRetryCount)
            .OrderBy(i => i.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// 标记图片为已同步（上传成功）
    /// </summary>
    public void MarkSynced(Guid imageId)
    {
        using var db = _dbFactory();
        var image = db.Images.Find(imageId);
        if (image == null) return;

        image.Synced = true;
        db.Images.Update(image);
        db.SaveChangesWithLock();
    }

    /// <summary>
    /// 标记图片上传失败（增加重试计数）
    /// </summary>
    public void MarkFailed(Guid imageId)
    {
        using var db = _dbFactory();
        var image = db.Images.Find(imageId);
        if (image == null) return;

        image.RetryCount++;
        db.Images.Update(image);
        db.SaveChangesWithLock();
    }

    /// <summary>
    /// 获取某笔记的所有图片
    /// </summary>
    public List<LocalImage> GetImagesForNote(Guid noteId)
    {
        using var db = _dbFactory();
        return db.Images
            .Where(i => i.NoteId == noteId)
            .OrderBy(i => i.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// 根据文件扩展名推断 MIME 类型
    /// </summary>
    private static string GuessContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}
