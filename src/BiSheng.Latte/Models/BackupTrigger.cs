namespace BiSheng.Latte.Models;

/// <summary>本地数据库备份触发来源</summary>
public enum BackupTrigger
{
    /// <summary>应用退出时</summary>
    Exit,

    /// <summary>启动/定时策略触发</summary>
    Scheduled,

    /// <summary>用户在备份管理中手动触发</summary>
    Manual,
}
