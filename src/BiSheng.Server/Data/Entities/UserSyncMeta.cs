using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BiSheng.Server.Data.Entities;

/// <summary>
/// 用户级同步元数据：记录 SyncLog 裁剪上界
/// </summary>
public class UserSyncMeta
{
    /// <summary>用户 ID</summary>
    [Key]
    public Guid UserId { get; set; }

    /// <summary>已删除 SyncLog 的版本上界（Version &lt; LogRetentionFloor 的记录已被裁剪）</summary>
    public long LogRetentionFloor { get; set; }

    /// <summary>用户 SyncLog 单调递增版本计数器（原子分配，替代 MAX(Version)+1）</summary>
    public long CurrentVersion { get; set; }

    /// <summary>关联用户</summary>
    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;
}
