using System.IO;
using System.Text;

namespace BiSheng.Latte.Services;

/// <summary>从 exe 旁旧布局迁移到 LocalAppData 的结果</summary>
public sealed class LatteLegacyMigrationResult
{
    /// <summary>是否执行了任何复制/重命名</summary>
    public bool HadWork { get; init; }

    /// <summary>人类可读摘要（供启动日志）</summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>
/// 将 exe 目录旁的 local.db / config.json / images 迁到 LocalAppData。
/// 目标已存在则跳过对应项；成功后将源文件改名为 *.bak-before-appdata，避免下次误用旧副本。
/// </summary>
public static class LatteLegacyDataMigrator
{
    private const string BakSuffix = ".bak-before-appdata";

    /// <summary>
    /// 若需要则执行迁移。须在打开数据库与初始化日志目录之前调用。
    /// </summary>
    /// <param name="legacyRoot">旧数据根（默认 exe 目录）</param>
    public static LatteLegacyMigrationResult MigrateIfNeeded(string? legacyRoot = null)
    {
        LatteAppPaths.EnsureRoot();
        var sourceRoot = string.IsNullOrWhiteSpace(legacyRoot)
            ? AppDomain.CurrentDomain.BaseDirectory
            : legacyRoot;

        if (LatteAppPaths.IsSameDirectory(sourceRoot, LatteAppPaths.Root))
        {
            return new LatteLegacyMigrationResult
            {
                HadWork = false,
                Summary = "用户数据已在 LocalAppData，无需迁移"
            };
        }

        var notes = new List<string>();
        var hadWork = false;

        hadWork |= TryMigrateSqliteBundle(sourceRoot, notes);
        hadWork |= TryMigrateFile(
            Path.Combine(sourceRoot, "config.json"),
            LatteAppPaths.ConfigFile,
            "config.json",
            notes);
        hadWork |= TryMigrateDirectory(
            Path.Combine(sourceRoot, "images"),
            LatteAppPaths.ImagesDirectory,
            "images",
            notes);

        if (notes.Count == 0)
        {
            return new LatteLegacyMigrationResult
            {
                HadWork = false,
                Summary = "未发现需迁移的 exe 旁用户数据"
            };
        }

        var sb = new StringBuilder();
        sb.Append(hadWork
            ? "已将旧版 exe 旁数据迁移到 LocalAppData："
            : "检查 exe 旁旧数据：");
        sb.Append(string.Join("；", notes));
        return new LatteLegacyMigrationResult
        {
            HadWork = hadWork,
            Summary = sb.ToString()
        };
    }

    /// <summary>迁移 local.db 及 WAL 附属文件</summary>
    private static bool TryMigrateSqliteBundle(string sourceRoot, List<string> notes)
    {
        var srcDb = Path.Combine(sourceRoot, "local.db");
        var destDb = LatteAppPaths.DatabaseFile;
        if (!File.Exists(srcDb))
        {
            return false;
        }

        if (File.Exists(destDb))
        {
            notes.Add("local.db 目标已存在，跳过");
            return false;
        }

        File.Copy(srcDb, destDb, overwrite: false);
        TryRenameWithBak(srcDb);

        foreach (var suffix in new[] { "-shm", "-wal" })
        {
            var srcSide = srcDb + suffix;
            var destSide = destDb + suffix;
            if (!File.Exists(srcSide))
            {
                continue;
            }

            if (!File.Exists(destSide))
            {
                File.Copy(srcSide, destSide, overwrite: false);
            }

            TryRenameWithBak(srcSide);
        }

        notes.Add("local.db");
        return true;
    }

    /// <summary>单文件：目标不存在则复制，成功后重命名源文件</summary>
    private static bool TryMigrateFile(string sourcePath, string destPath, string label, List<string> notes)
    {
        if (!File.Exists(sourcePath))
        {
            return false;
        }

        if (File.Exists(destPath))
        {
            notes.Add($"{label} 目标已存在，跳过");
            return false;
        }

        var destDir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(sourcePath, destPath, overwrite: false);
        TryRenameWithBak(sourcePath);
        notes.Add(label);
        return true;
    }

    /// <summary>目录：合并复制缺失文件；源目录在有文件被迁走后整体改名</summary>
    private static bool TryMigrateDirectory(string sourceDir, string destDir, string label, List<string> notes)
    {
        if (!Directory.Exists(sourceDir))
        {
            return false;
        }

        Directory.CreateDirectory(destDir);
        var copied = 0;
        foreach (var srcFile in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, srcFile);
            var destFile = Path.Combine(destDir, relative);
            if (File.Exists(destFile))
            {
                continue;
            }

            var parent = Path.GetDirectoryName(destFile);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.Copy(srcFile, destFile, overwrite: false);
            copied++;
        }

        if (copied == 0)
        {
            // 目标已齐：仍把源目录改名，避免安装目录残留可写数据被误用
            if (!Directory.Exists(sourceDir + BakSuffix))
            {
                TryRenameDirectoryWithBak(sourceDir);
                notes.Add($"{label} 目标已存在，已归档 exe 旁目录");
                return true;
            }

            notes.Add($"{label} 目标已存在，跳过");
            return false;
        }

        TryRenameDirectoryWithBak(sourceDir);
        notes.Add($"{label}（{copied} 个文件）");
        return true;
    }

    /// <summary>将源文件改名为 *.bak-before-appdata</summary>
    private static void TryRenameWithBak(string path)
    {
        try
        {
            var bak = path + BakSuffix;
            if (File.Exists(bak))
            {
                File.Delete(bak);
            }

            File.Move(path, bak);
        }
        catch
        {
            // 改名失败不阻断启动；目标侧已有可用副本
        }
    }

    /// <summary>将源目录改名为 *.bak-before-appdata</summary>
    private static void TryRenameDirectoryWithBak(string directory)
    {
        try
        {
            var bak = directory + BakSuffix;
            if (Directory.Exists(bak))
            {
                Directory.Delete(bak, recursive: true);
            }

            Directory.Move(directory, bak);
        }
        catch
        {
            // 同上
        }
    }
}
