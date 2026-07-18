using System.IO;
using BiSheng.Latte.Models;
using Microsoft.Data.Sqlite;

namespace BiSheng.Latte.Services;

/// <summary>只读分析 local.db / 备份文件：统计、完整性检查</summary>
public static class LocalDatabaseBackupInspector
{
    /// <summary>采集数据库内容统计</summary>
    public static LocalDatabaseBackupStats CollectStats(string dbPath)
    {
        var stats = new LocalDatabaseBackupStats();
        if (!File.Exists(dbPath))
        {
            return stats;
        }

        try
        {
            using var conn = OpenReadOnly(dbPath);
            stats.NoteCount = ScalarInt(conn, "SELECT COUNT(*) FROM Notes");
            stats.FolderCount = ScalarInt(conn, "SELECT COUNT(*) FROM Folders");
            stats.DeletedNoteCount = ScalarInt(conn, "SELECT COUNT(*) FROM Notes WHERE IsDeleted = 1");
            stats.ImageMetaCount = ScalarInt(conn, "SELECT COUNT(*) FROM Images");
            stats.RevisionCount = ScalarInt(conn, "SELECT COUNT(*) FROM NoteRevisions");
            stats.PendingChangeCount = ScalarInt(conn, "SELECT COUNT(*) FROM PendingChanges");
            stats.ContentBytesApprox = ScalarLong(conn,
                @"SELECT COALESCE((SELECT SUM(LENGTH(Title) + LENGTH(Content)) FROM Notes), 0)
                  + COALESCE((SELECT SUM(LENGTH(Title) + LENGTH(Content)) FROM NoteRevisions), 0)");
        }
        catch (Exception ex)
        {
            LogHelper.Warn("备份统计失败: {0} ({1})", dbPath, ex.Message);
        }

        return stats;
    }

    /// <summary>执行 integrity_check</summary>
    public static bool CheckIntegrity(string dbPath)
    {
        if (!File.Exists(dbPath))
        {
            return false;
        }

        try
        {
            using var conn = OpenReadOnly(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check;";
            var result = cmd.ExecuteScalar()?.ToString();
            return string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            LogHelper.Warn("integrity_check 失败: {0} ({1})", dbPath, ex.Message);
            return false;
        }
    }

    /// <summary>为无 sidecar 的备份生成 manifest 并可选落盘</summary>
    public static LocalDatabaseBackupManifest BuildManifestForBackupFile(
        string backupDbPath,
        bool persistMeta)
    {
        var fileInfo = new FileInfo(backupDbPath);
        var manifest = new LocalDatabaseBackupManifest
        {
            SchemaVersion = 1,
            BackupFileName = fileInfo.Name,
            CreatedAtUtc = fileInfo.LastWriteTimeUtc,
            Trigger = BackupTrigger.Manual,
            AppVersion = LocalDatabaseBackupManifest.CurrentAppVersion(),
            SourceDbPath = string.Empty,
            FileSizeBytes = fileInfo.Length,
            IntegrityOk = CheckIntegrity(backupDbPath),
            Stats = CollectStats(backupDbPath),
            Vacuum = new LocalDatabaseBackupVacuumInfo
            {
                Method = "Legacy",
            },
        };

        if (persistMeta)
        {
            manifest.Save(backupDbPath);
        }

        return manifest;
    }

    private static SqliteConnection OpenReadOnly(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        conn.Open();
        return conn;
    }

    private static int ScalarInt(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static long ScalarLong(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }
}
