using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BiSheng.Server.Data.Entities;

/// <summary>
/// 服务端图片实体：记录用户上传的图片文件元数据
/// 文件存储在磁盘 uploads/{UserId}/{Id}.{ext}
/// </summary>
public class ServerImage
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public Guid UserId { get; set; }

    /// <summary>原始文件名</summary>
    [Required, MaxLength(256)]
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME 类型（如 image/png）</summary>
    [Required, MaxLength(64)]
    public string ContentType { get; set; } = "image/png";

    /// <summary>文件大小（字节）</summary>
    public long FileSize { get; set; }

    /// <summary>扩展名（如 .png）</summary>
    [MaxLength(16)]
    public string Extension { get; set; } = ".png";

    /// <summary>是否已标记删除</summary>
    public bool IsDeleted { get; set; }

    /// <summary>标记删除时间（用于延迟 GC）</summary>
    public DateTime? DeletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
