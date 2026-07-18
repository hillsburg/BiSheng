using System.ComponentModel.DataAnnotations;

namespace BiSheng.Server.DTOs;

public record FolderDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsPinned { get; init; }
    public bool IsDeleted { get; init; }
    public long Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateFolderRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
}

public record UpdateFolderRequest
{
    [Required, StringLength(128, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
}
