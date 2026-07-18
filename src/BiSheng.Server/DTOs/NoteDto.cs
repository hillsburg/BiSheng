using System.ComponentModel.DataAnnotations;

namespace BiSheng.Server.DTOs;

public record NoteDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public Guid FolderId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsPinned { get; init; }
    public bool IsDeleted { get; init; }
    public long Version { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record NoteListItemDto
{
    public Guid Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public Guid FolderId { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsPinned { get; init; }
    public bool IsDeleted { get; init; }
    public long Version { get; init; }
    public DateTime UpdatedAt { get; init; }
}

public record CreateNoteRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    [Required]
    public Guid FolderId { get; init; }
}

public record UpdateNoteRequest
{
    [Required, StringLength(256, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    [Required]
    public Guid FolderId { get; init; }
}
