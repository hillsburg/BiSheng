using System.IO;
using BiSheng.Latte.Models;

namespace BiSheng.Latte.Services;

/// <summary>本地 SQLite 与备份目录路径</summary>
public static class LocalDatabasePaths
{
    /// <summary>默认备份目录（LocalAppData）</summary>
    public static string DefaultBackupDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BiSheng", "Latte", "backups");

    /// <summary>运行中的 local.db 路径</summary>
    public static string DatabaseFile =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "local.db");

    /// <summary>兼容旧代码：等同默认备份目录</summary>
    public static string BackupDirectory => DefaultBackupDirectory;

    /// <summary>根据数据安全设置解析生效的备份目录</summary>
    public static string ResolveBackupDirectory(DataSafetySettings settings)
    {
        if (settings.BackupDirectoryUseDefault || string.IsNullOrWhiteSpace(settings.BackupDirectory))
        {
            return DefaultBackupDirectory;
        }

        return Path.GetFullPath(settings.BackupDirectory.Trim());
    }

    /// <summary>校验备份目录可写；必要时创建目录</summary>
    public static bool TryEnsureBackupDirectory(string directory, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "备份目录不能为空";
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".bisheng-write-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex)
        {
            error = $"无法写入备份目录：{ex.Message}";
            return false;
        }
    }

    /// <summary>备份目录是否位于应用目录内（存在误删风险）</summary>
    public static bool IsBackupDirectoryInsideApp(string directory)
    {
        var appRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var backupRoot = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return backupRoot.StartsWith(appRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
               || string.Equals(backupRoot, appRoot, StringComparison.OrdinalIgnoreCase);
    }
}
