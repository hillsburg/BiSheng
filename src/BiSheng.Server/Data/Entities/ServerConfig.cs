using System.ComponentModel.DataAnnotations;

namespace BiSheng.Server.Data.Entities;

/// <summary>
/// 服务器全局配置（单例实体，仅一行，Id=1）
/// </summary>
public class ServerConfig
{
    [Key]
    public int Id { get; set; } = 1;

    /// <summary>是否已完成初始化设置</summary>
    public bool IsSetup { get; set; }

    public DateTime? SetupAt { get; set; }

    /// <summary>图片软删除保留天数（默认30天，到期后 GC 服务清理磁盘文件）</summary>
    public int ImageRetentionDays { get; set; } = 30;

    /// <summary>单张图片最大大小（MB，默认10）</summary>
    public int MaxImageSizeMb { get; set; } = 10;

    /// <summary>客户端超过此天数未同步则标记为 stale，不再拖住 SyncLog 裁剪线</summary>
    public int SyncLogStaleClientDays { get; set; } = 90;

    /// <summary>用户 SyncLog 行数低于此值时不执行裁剪</summary>
    public int SyncLogMinEntriesForCompaction { get; set; } = 1000;

    /// <summary>SyncLog 裁剪后台任务间隔（小时）</summary>
    public int SyncLogCompactionIntervalHours { get; set; } = 24;
}
