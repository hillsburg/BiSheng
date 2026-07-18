using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BiSheng.Server.Data.Entities;

public class Note
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(256)]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    [Required]
    public Guid FolderId { get; set; }

    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }

    [Required]
    public Guid UserId { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>
    /// 单调递增版本号，用于增量同步
    /// </summary>
    public long Version { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(FolderId))]
    public Folder Folder { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
