using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BiSheng.Server.Data.Entities;

public class Folder
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

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
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ParentId))]
    public Folder? Parent { get; set; }

    public ICollection<Folder> Children { get; set; } = new List<Folder>();
    public ICollection<Note> Notes { get; set; } = new List<Note>();
}
