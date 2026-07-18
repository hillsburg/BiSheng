using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BiSheng.Server.Data.Entities;

/// <summary>
/// 客户端（API Key / 设备）同步状态，用于 SyncLog 安全裁剪基线计算
/// </summary>
public class ClientSyncState
{
    [Key]
    public Guid ApiKeyId { get; set; }

    [Required]
    public Guid UserId { get; set; }

    /// <summary>该设备最后成功同步到的服务端版本号</summary>
    public long LastSyncVersion { get; set; }

    /// <summary>最后一次 Push/Pull/version 请求时间</summary>
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>长期离线设备：不参与 min(LastSyncVersion) 裁剪基线</summary>
    public bool IsStaleExcluded { get; set; }

    [ForeignKey(nameof(ApiKeyId))]
    public ApiKey ApiKey { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
