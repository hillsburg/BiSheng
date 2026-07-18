using System.IO;
using System.Text.Json;

namespace BiSheng.Latte.Models;

/// <summary>local.db 备份 sidecar 元数据（与 .db 同目录，文件名 + .meta.json）</summary>
public class LocalDatabaseBackupManifest
{
    /// <summary>Sidecar  schema 版本</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>备份文件名（不含路径）</summary>
    public string BackupFileName { get; set; } = string.Empty;

    /// <summary>备份创建时间（UTC）</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>触发来源</summary>
    public BackupTrigger Trigger { get; set; }

    /// <summary>创建备份时的应用版本</summary>
    public string AppVersion { get; set; } = string.Empty;

    /// <summary>源库路径</summary>
    public string SourceDbPath { get; set; } = string.Empty;

    /// <summary>备份文件大小（字节）</summary>
    public long FileSizeBytes { get; set; }

    /// <summary>VACUUM 相关统计</summary>
    public LocalDatabaseBackupVacuumInfo Vacuum { get; set; } = new();

    /// <summary>备份文件 integrity_check 是否通过</summary>
    public bool IntegrityOk { get; set; }

    /// <summary>库内内容统计</summary>
    public LocalDatabaseBackupStats Stats { get; set; } = new();

    /// <summary>sidecar 文件路径</summary>
    public static string GetMetaPath(string backupDbPath) => backupDbPath + ".meta.json";

    /// <summary>读取 sidecar；不存在或解析失败返回 null</summary>
    public static LocalDatabaseBackupManifest? TryLoad(string backupDbPath)
    {
        var metaPath = GetMetaPath(backupDbPath);
        if (!File.Exists(metaPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(metaPath);
            return JsonSerializer.Deserialize<LocalDatabaseBackupManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>写入 sidecar</summary>
    public void Save(string backupDbPath)
    {
        BackupFileName = Path.GetFileName(backupDbPath);
        var metaPath = GetMetaPath(backupDbPath);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(metaPath, json);
    }

    /// <summary>当前程序集版本字符串</summary>
    public static string CurrentAppVersion() =>
        Services.LatteAppVersion.DisplayVersion;

    /// <summary>触发来源的中文标签</summary>
    public static string GetTriggerLabel(BackupTrigger trigger) => trigger switch
    {
        BackupTrigger.Exit => "退出时",
        BackupTrigger.Scheduled => "定时",
        BackupTrigger.Manual => "手动",
        _ => "未知",
    };
}

/// <summary>备份前 VACUUM 统计</summary>
public class LocalDatabaseBackupVacuumInfo
{
    /// <summary>压缩前源库大小</summary>
    public long SourceSizeBeforeBytes { get; set; }

    /// <summary>压缩后源库大小</summary>
    public long SourceSizeAfterBytes { get; set; }

    /// <summary>备份写入方式</summary>
    public string Method { get; set; } = "VacuumInto";
}

/// <summary>备份库内容统计</summary>
public class LocalDatabaseBackupStats
{
    /// <summary>笔记数量（含软删）</summary>
    public int NoteCount { get; set; }

    /// <summary>文件夹数量</summary>
    public int FolderCount { get; set; }

    /// <summary>软删笔记数</summary>
    public int DeletedNoteCount { get; set; }

    /// <summary>图片元数据条数</summary>
    public int ImageMetaCount { get; set; }

    /// <summary>本地历史版本条数</summary>
    public int RevisionCount { get; set; }

    /// <summary>待同步变更条数</summary>
    public int PendingChangeCount { get; set; }

    /// <summary>笔记+历史正文近似字节</summary>
    public long ContentBytesApprox { get; set; }
}
