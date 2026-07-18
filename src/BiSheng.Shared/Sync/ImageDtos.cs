using System;
using System.Collections.Generic;

namespace BiSheng.Shared.Sync;

/// <summary>GET /api/images/pending 响应</summary>
public record ImagePendingResponse
{
    public List<ImagePendingDto> Images { get; init; } = new();
    public DateTime ServerTime { get; init; }
}

/// <summary>图片增量拉取条目</summary>
public record ImagePendingDto
{
    public Guid Id { get; init; }
    public bool IsDeleted { get; init; }
    public string Extension { get; init; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? DeletedAt { get; init; }
}
