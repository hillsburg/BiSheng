using System.ComponentModel.DataAnnotations;

namespace BiSheng.Latte.Data.Entities;

public class LocalNote
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [Required]
    public Guid FolderId { get; set; }

    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>软删除时间（回收站）；旧数据可为 null，回退到 UpdatedAt</summary>
    public DateTime? DeletedAt { get; set; }

    public long Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
