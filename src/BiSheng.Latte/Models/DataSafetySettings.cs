using System.IO;
using System.Text.Json;

namespace BiSheng.Latte.Models;

/// <summary>
/// 本地数据安全策略：自动备份、编辑日志、回收站与导出提醒
/// </summary>
public class DataSafetySettings
{
    /// <summary>安全档位（标准 / 保守）</summary>
    public DataSafetyProfile Profile { get; set; } = DataSafetyProfile.Balanced;

    /// <summary>是否启用自动备份</summary>
    public bool EnableAutoBackup { get; set; } = true;

    /// <summary>关闭应用时是否备份</summary>
    public bool BackupOnExit { get; set; } = true;

    /// <summary>保留的备份文件份数（超出则删最旧）</summary>
    public int BackupRetentionCount { get; set; } = 14;

    /// <summary>定时备份间隔（小时）；0 表示仅退出时备份</summary>
    public int BackupIntervalHours { get; set; } = 24;

    /// <summary>是否使用默认备份目录（LocalAppData）</summary>
    public bool BackupDirectoryUseDefault { get; set; } = true;

    /// <summary>自定义备份目录绝对路径</summary>
    public string BackupDirectory { get; set; } = string.Empty;

    /// <summary>上次成功备份时间（UTC）</summary>
    public DateTime? LastBackupUtc { get; set; }

    /// <summary>是否记录只追加编辑日志</summary>
    public bool EnableEditJournal { get; set; } = true;

    /// <summary>编辑日志保留天数</summary>
    public int EditJournalRetentionDays { get; set; } = 30;

    /// <summary>回收站保留天数（到期本地硬删）</summary>
    public int TrashRetentionDays { get; set; } = 30;

    /// <summary>是否提示定期导出全库</summary>
    public bool EnableExportReminder { get; set; } = true;

    /// <summary>超过此天数未全库导出则提醒</summary>
    public int RemindExportAfterDays { get; set; } = 7;

    /// <summary>上次成功全库导出时间（UTC）</summary>
    public DateTime? LastFullExportUtc { get; set; }

    private static string SettingsPath =>
        Path.Combine(Services.LatteAppPaths.Root, "data-safety.json");

    public static DataSafetySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var settings = JsonSerializer.Deserialize<DataSafetySettings>(json) ?? new DataSafetySettings();
                settings.Normalize();
                return settings;
            }
        }
        catch
        {
            /* 解析失败则返回默认值 */
        }

        var defaults = new DataSafetySettings();
        defaults.Normalize();
        return defaults;
    }

    public void Save()
    {
        Normalize();

        var dir = Path.GetDirectoryName(SettingsPath)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
    }

    public void Normalize()
    {
        BackupRetentionCount = Math.Clamp(BackupRetentionCount, 3, 90);
        BackupIntervalHours = Math.Clamp(BackupIntervalHours, 0, 168);
        EditJournalRetentionDays = Math.Clamp(EditJournalRetentionDays, 7, 365);
        TrashRetentionDays = Math.Clamp(TrashRetentionDays, 7, 365);
        RemindExportAfterDays = Math.Clamp(RemindExportAfterDays, 1, 90);

        if (BackupDirectoryUseDefault)
        {
            BackupDirectory = string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(BackupDirectory))
        {
            BackupDirectory = Path.GetFullPath(BackupDirectory.Trim());
        }
    }

    /// <summary>记录一次成功全库导出</summary>
    public void RecordFullExport()
    {
        LastFullExportUtc = DateTime.UtcNow;
        Save();
    }

    /// <summary>是否应显示导出提醒</summary>
    public bool ShouldRemindExport()
    {
        if (!EnableExportReminder)
        {
            return false;
        }

        if (!LastFullExportUtc.HasValue)
        {
            return true;
        }

        return DateTime.UtcNow - LastFullExportUtc.Value >= TimeSpan.FromDays(RemindExportAfterDays);
    }

    /// <summary>深拷贝（供备份管理等对话框使用未保存的设置快照）</summary>
    public DataSafetySettings Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<DataSafetySettings>(json)!;
    }
}
