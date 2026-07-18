using BiSheng.Server.Auth;
using BiSheng.Server.Data;
using BiSheng.Server.Data.Entities;
using BiSheng.Server.Services.Images;
using BiSheng.Shared.Sync;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BiSheng.Server.Controllers;

/// <summary>
/// 图片同步控制器：支持按 UUID 上传/下载/软删除图片
/// 图片同步管道独立于笔记文本同步管道
/// </summary>
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
[ApiController]
[Route("api/[controller]")]
public class ImagesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ImagesController> _logger;
    private static readonly string UploadDir = "uploads";

    /// <summary>允许的图片扩展名</summary>
    private static readonly string[] AllowedExts =
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private Guid UserId => User.GetUserId();

    public ImagesController(AppDbContext db, IWebHostEnvironment env, ILogger<ImagesController> logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    // ==========================================================
    //  上传
    // ==========================================================

    /// <summary>
    /// 上传图片：Id 由客户端指定（UUID），文件存储在 uploads/{userId}/{id}{ext}；
    /// 扩展名白名单之外拒绝，并通过文件头魔数校验防止伪装格式
    /// </summary>
    [HttpPost("{id:guid}")]
    [RequestSizeLimit(15_000_000)] // 请求体上限 15MB（含 multipart 开销）
    public async Task<IActionResult> Upload(Guid id, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "文件为空" });
        }

        // 校验扩展名
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExts.Contains(ext))
        {
            return BadRequest(new { message = $"不支持的文件格式: {ext}" });
        }

        // 从 ServerConfig 读取最大大小限制
        var config = await _db.ServerConfigs.FirstOrDefaultAsync() ?? new ServerConfig();
        var maxBytes = config.MaxImageSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
        {
            return BadRequest(new { message = $"图片超过 {config.MaxImageSizeMb}MB 限制" });
        }

        // 魔数校验：扩展名须与真实文件头一致
        var header = new byte[ImageContentValidator.MinHeaderLength];
        await using (var probe = file.OpenReadStream())
        {
            var read = await probe.ReadAsync(header.AsMemory(0, header.Length));
            if (read < header.Length)
            {
                Array.Resize(ref header, read);
            }
        }

        if (!ImageContentValidator.TryValidate(header, ext, out var contentType, out var magicError))
        {
            return BadRequest(new { message = magicError });
        }

        var userDir = Path.Combine(_env.ContentRootPath, UploadDir, UserId.ToString());
        if (!Directory.Exists(userDir))
        {
            Directory.CreateDirectory(userDir);
        }

        var diskFileName = $"{id}{ext}";
        var filePath = Path.Combine(userDir, diskFileName);

        // 已存在：活跃行幂等成功；软删行则复活并重写文件（避免 200 后 GET 仍 404）
        var existing = await _db.Images.FirstOrDefaultAsync(i => i.Id == id && i.UserId == UserId);
        if (existing != null)
        {
            if (!existing.IsDeleted)
            {
                return Ok(new { id = existing.Id, url = $"/api/images/{existing.Id}" });
            }

            await WriteUploadFileAsync(file, filePath);
            existing.IsDeleted = false;
            existing.DeletedAt = null;
            existing.FileName = file.FileName;
            existing.ContentType = contentType;
            existing.FileSize = file.Length;
            existing.Extension = ext;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch
            {
                TryDeleteFile(filePath);
                throw;
            }

            _logger.LogInformation("软删图片已复活上传: {ImageId}, 用户: {UserId}", id, UserId);
            return Ok(new { id = existing.Id, url = $"/api/images/{existing.Id}" });
        }

        // 新图：先落盘再写库；SaveChanges 失败则删除孤儿文件
        await WriteUploadFileAsync(file, filePath);

        var image = new ServerImage
        {
            Id = id,
            UserId = UserId,
            FileName = file.FileName,
            ContentType = contentType,
            FileSize = file.Length,
            Extension = ext,
            CreatedAt = DateTime.UtcNow
        };
        _db.Images.Add(image);

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            TryDeleteFile(filePath);
            throw;
        }

        _logger.LogInformation("图片已上传: {ImageId}, 用户: {UserId}, 大小: {Size}", id, UserId, file.Length);
        return Ok(new { id = image.Id, url = $"/api/images/{image.Id}" });
    }

    /// <summary>将上传流写入目标路径</summary>
    private static async Task WriteUploadFileAsync(IFormFile file, string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream);
    }

    /// <summary>尽力删除磁盘文件（失败仅记日志场景由调用方处理）</summary>
    private void TryDeleteFile(string filePath)
    {
        try
        {
            if (System.IO.File.Exists(filePath))
            {
                System.IO.File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "清理上传孤儿文件失败: {Path}", filePath);
        }
    }

    // ==========================================================
    //  下载
    // ==========================================================

    /// <summary>
    /// 下载图片文件
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download(Guid id)
    {
        var image = await _db.Images.FirstOrDefaultAsync(i => i.Id == id && i.UserId == UserId);
        if (image == null || image.IsDeleted)
            return NotFound();

        var filePath = GetDiskPath(image);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "文件不存在" });

        return PhysicalFile(filePath, image.ContentType, image.FileName);
    }

    // ==========================================================
    //  软删除
    // ==========================================================

    /// <summary>
    /// 标记图片为软删除（磁盘文件保留 ImageRetentionDays 天后由 GC 清理）
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var image = await _db.Images.FirstOrDefaultAsync(i => i.Id == id && i.UserId == UserId);
        if (image == null)
            return NotFound();

        if (!image.IsDeleted)
        {
            image.IsDeleted = true;
            image.DeletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "已标记删除" });
    }

    // ==========================================================
    //  增量拉取
    // ==========================================================

    /// <summary>
    /// 查询自指定时间以来新增/删除的图片列表（供客户端增量同步）
    /// </summary>
    [HttpGet("pending")]
    public async Task<IActionResult> GetPending([FromQuery] DateTime? since)
    {
        var sinceTime = since ?? DateTime.MinValue;

        var images = await _db.Images
            .Where(i => i.UserId == UserId
                && (i.CreatedAt > sinceTime
                    || (i.IsDeleted && i.DeletedAt != null && i.DeletedAt > sinceTime)))
            .Select(i => new ImagePendingDto
            {
                Id = i.Id,
                IsDeleted = i.IsDeleted,
                Extension = i.Extension,
                ContentType = i.ContentType,
                FileSize = i.FileSize,
                CreatedAt = i.CreatedAt,
                DeletedAt = i.DeletedAt
            })
            .ToListAsync();

        return Ok(new ImagePendingResponse
        {
            Images = images,
            ServerTime = DateTime.UtcNow
        });
    }

    // ==========================================================
    //  辅助方法
    // ==========================================================

    private string GetDiskPath(ServerImage image)
    {
        return Path.Combine(_env.ContentRootPath, UploadDir,
            UserId.ToString(), $"{image.Id}{image.Extension}");
    }
}
