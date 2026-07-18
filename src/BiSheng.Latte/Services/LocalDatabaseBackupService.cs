using System.IO;
using BiSheng.Latte.Models;
using Microsoft.Data.Sqlite;

namespace BiSheng.Latte.Services;

/// <summary>
/// 本地数据库备份：备份前 VACUUM 源库，VACUUM INTO 紧凑快照，并写入 sidecar 元数据
/// </summary>
public static class LocalDatabaseBackupService
{
    /// <summary>按策略尝试备份；成功时更新 settings.LastBackupUtc 并持久化</summary>
    public static bool TryRunScheduledBackup(DataSafetySettings settings, bool onExit)
    {
        if (!settings.EnableAutoBackup)
        {
            return false;
        }

        if (onExit)
        {
            if (!settings.BackupOnExit)
            {
                return false;
            }
        }
        else if (!ShouldRunPeriodicBackup(settings))
        {
            return false;
        }

        var trigger = onExit ? BackupTrigger.Exit : BackupTrigger.Scheduled;
        if (!TryCreateBackup(settings, trigger, out var backupPath))
        {
            return false;
        }

        settings.LastBackupUtc = DateTime.UtcNow;
        settings.Save();
        LogHelper.Info("本地数据库已备份: {0}", backupPath);
        return true;
    }

    private static bool ShouldRunPeriodicBackup(DataSafetySettings settings)
    {
        if (settings.BackupIntervalHours <= 0)
        {
            return false;
        }

        if (!settings.LastBackupUtc.HasValue)
        {
            return true;
        }

        return DateTime.UtcNow - settings.LastBackupUtc.Value
            >= TimeSpan.FromHours(settings.BackupIntervalHours);
    }

    /// <summary>创建一份带时间戳的紧凑备份文件</summary>
    public static bool TryCreateBackup(DataSafetySettings settings, BackupTrigger trigger, out string backupPath)
    {
        backupPath = string.Empty;
        var sourcePath = LocalDatabasePaths.DatabaseFile;
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        var backupDir = LocalDatabasePaths.ResolveBackupDirectory(settings);
        if (!LocalDatabasePaths.TryEnsureBackupDirectory(backupDir, out var dirError))
        {
            LogHelper.Warn("备份目录不可用: {0}", dirError);
            return false;
        }

        try
        {
            backupPath = Path.Combine(backupDir, $"local-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");
            var stats = LocalDatabaseBackupInspector.CollectStats(sourcePath);
            var sourceSizeBefore = new FileInfo(sourcePath).Length;

            TryWalCheckpoint(sourcePath);
            TryVacuumSource(sourcePath, sourceSizeBefore, out var sourceSizeAfter);

            var vacuumMethod = "VacuumInto";
            var created = TryVacuumIntoBackup(sourcePath, backupPath);
            if (!created)
            {
                vacuumMethod = "BackupApi+Vacuum";
                created = TryBackupViaCompactTemp(sourcePath, backupPath);
            }

            if (!created)
            {
                return false;
            }

            var integrityOk = LocalDatabaseBackupInspector.CheckIntegrity(backupPath);
            var manifest = new LocalDatabaseBackupManifest
            {
                CreatedAtUtc = DateTime.UtcNow,
                Trigger = trigger,
                AppVersion = LocalDatabaseBackupManifest.CurrentAppVersion(),
                SourceDbPath = sourcePath,
                FileSizeBytes = new FileInfo(backupPath).Length,
                IntegrityOk = integrityOk,
                Stats = stats,
                Vacuum = new LocalDatabaseBackupVacuumInfo
                {
                    SourceSizeBeforeBytes = sourceSizeBefore,
                    SourceSizeAfterBytes = sourceSizeAfter,
                    Method = vacuumMethod,
                },
            };
            manifest.Save(backupPath);

            PruneOldBackups(backupDir, settings.BackupRetentionCount);
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error("本地数据库备份失败", ex);
            DeleteFileIfExists(backupPath);
            DeleteFileIfExists(LocalDatabaseBackupManifest.GetMetaPath(backupPath));
            return false;
        }
    }

    /// <summary>将 WAL 落盘并截断，便于后续 VACUUM</summary>
    private static void TryWalCheckpoint(string sourcePath)
    {
        try
        {
            using var conn = new SqliteConnection($"Data Source={sourcePath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            LogHelper.Warn("WAL checkpoint 失败: {0}", ex.Message);
        }
    }

    /// <summary>备份前压缩源库，回收 freelist 空洞页</summary>
    private static void TryVacuumSource(string sourcePath, long sizeBefore, out long sizeAfter)
    {
        sizeAfter = sizeBefore;
        try
        {
            SqliteConnection.ClearAllPools();

            if (!TryVacuumDatabase(sourcePath))
            {
                return;
            }

            sizeAfter = new FileInfo(sourcePath).Length;
            LogHelper.Info(
                "本地数据库已压缩: {0} ({1:F2} MB → {2:F2} MB)",
                sourcePath,
                sizeBefore / 1024.0 / 1024.0,
                sizeAfter / 1024.0 / 1024.0);
        }
        catch (Exception ex)
        {
            LogHelper.Warn("备份前 VACUUM 源库失败（将继续备份）: {0}", ex.Message);
        }
    }

    /// <summary>VACUUM INTO：直接写出紧凑备份（首选）</summary>
    private static bool TryVacuumIntoBackup(string sourcePath, string backupPath)
    {
        DeleteFileIfExists(backupPath);

        try
        {
            var escaped = EscapeSqlitePath(backupPath);
            using var conn = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"VACUUM INTO '{escaped}'";
            cmd.ExecuteNonQuery();
            return File.Exists(backupPath);
        }
        catch (Exception ex)
        {
            LogHelper.Warn("VACUUM INTO 备份失败，将尝试临时库压缩: {0}", ex.Message);
            DeleteFileIfExists(backupPath);
            return false;
        }
    }

    /// <summary>Backup API 写入临时库后再 VACUUM（VACUUM INTO 不可用时的回退）</summary>
    private static bool TryBackupViaCompactTemp(string sourcePath, string backupPath)
    {
        var tempPath = backupPath + ".compact.tmp";
        DeleteFileIfExists(tempPath);

        try
        {
            using (var source = new SqliteConnection($"Data Source={sourcePath};Mode=ReadOnly"))
            using (var destination = new SqliteConnection($"Data Source={tempPath}"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);
            }

            if (!TryVacuumDatabase(tempPath))
            {
                return false;
            }

            DeleteFileIfExists(backupPath);
            File.Move(tempPath, backupPath);
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Error("临时库压缩备份失败", ex);
            DeleteFileIfExists(tempPath);
            return false;
        }
    }

    /// <summary>对指定数据库文件执行 VACUUM</summary>
    private static bool TryVacuumDatabase(string dbPath)
    {
        try
        {
            SqliteConnection.ClearAllPools();
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "VACUUM;";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch (Exception ex)
        {
            LogHelper.Warn("VACUUM 失败: {0} ({1})", dbPath, ex.Message);
            return false;
        }
    }

    private static void PruneOldBackups(string backupDir, int retentionCount)
    {
        var stale = Directory.GetFiles(backupDir, "local-*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Skip(retentionCount)
            .ToList();

        foreach (var file in stale)
        {
            try
            {
                file.Delete();
                DeleteFileIfExists(LocalDatabaseBackupManifest.GetMetaPath(file.FullName));
            }
            catch (Exception ex)
            {
                LogHelper.Warn("删除旧备份失败: {0} ({1})", file.FullName, ex.Message);
            }
        }
    }

    private static string EscapeSqlitePath(string path) =>
        path.Replace('\\', '/').Replace("'", "''");

    private static void DeleteFileIfExists(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            /* 忽略清理失败 */
        }
    }
}
