using System.ComponentModel.DataAnnotations;

namespace BiSheng.Latte.Data.Entities;

/// <summary>
/// 本地同步状态：记录最后同步版本号
/// 待推送变更已迁移到 LocalPendingChange 独立表（去重合并 + 避免 JSON 瓶颈）
/// </summary>
public class LocalSyncState
{
    [Key]
    public int Id { get; set; } = 1;

    /// <summary>
    /// 客户端最后一次成功同步的服务端版本号
    /// </summary>
    public long LastSyncVersion { get; set; }

    /// <summary>
    /// 最后一次图片增量拉取的时间（用于增量查询服务端新增的图片）
    /// </summary>
    public DateTime? LastImagePullTime { get; set; }
}
