using System.ComponentModel.DataAnnotations;

namespace BiSheng.Latte.Data.Entities;

/// <summary>
/// 本地图片实体：记录笔记中粘贴的每张图片的元数据
/// 
/// 数据流：
/// 粘贴图片 → 生成 UUID → 保存到 images/{uuid}.png → 写入本表(Synced=false)
/// → ImageSyncService 后台上传 → 标记 Synced=true
/// </summary>
public class LocalImage
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>所属笔记 ID</summary>
    public Guid NoteId { get; set; }

    /// <summary>原始文件名（如截图.png）</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>本地文件路径（如 images/{uuid}.png）</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; set; }

    /// <summary>MIME 类型（如 image/png）</summary>
    public string ContentType { get; set; } = "image/png";

    /// <summary>是否已成功上传到服务器</summary>
    public bool Synced { get; set; }

    /// <summary>上传失败重试次数</summary>
    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
