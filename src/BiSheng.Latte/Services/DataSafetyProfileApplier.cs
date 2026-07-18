using BiSheng.Latte.Models;

namespace BiSheng.Latte.Services;

/// <summary>将「保守模式」预设应用到同步与备份配置</summary>
public static class DataSafetyProfileApplier
{
    /// <summary>按档位写入同步与备份预设（保守模式覆盖 Push / 备份 / 编辑日志开关）</summary>
    public static void ApplyProfile(DataSafetyProfile profile, SyncSettings sync, DataSafetySettings safety)
    {
        if (profile == DataSafetyProfile.Conservative)
        {
            sync.PeriodicPushIntervalSeconds = 15;
            sync.FlushOnExit = true;
            safety.BackupIntervalHours = 12;
            safety.BackupRetentionCount = Math.Max(safety.BackupRetentionCount, 21);
            safety.EnableEditJournal = true;
        }

        sync.Normalize();
        safety.Normalize();
    }
}
