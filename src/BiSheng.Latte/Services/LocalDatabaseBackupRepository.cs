using System.Diagnostics;
using System.IO;
using BiSheng.Latte.Models;

namespace BiSheng.Latte.Services;

/// <summary>备份目录枚举、删除与资源管理器打开</summary>
public static class LocalDatabaseBackupRepository
{
    /// <summary>备份列表项（供 UI 绑定）</summary>
    public sealed class BackupListItem
    {
        /// <summary>备份 .db 完整路径</summary>
        public string FilePath { get; init; } = string.Empty;

        /// <summary>文件名</summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>创建时间（UTC）</summary>
        public DateTime CreatedAtUtc { get; init; }

        /// <summary>文件大小（字节）</summary>
        public long FileSizeBytes { get; init; }

        /// <summary>是否已有 sidecar</summary>
        public bool HasMeta { get; init; }

        /// <summary>sidecar 元数据</summary>
        public LocalDatabaseBackupManifest? Manifest { get; init; }

        /// <summary>是否为无 sidecar 的旧备份</summary>
        public bool IsLegacy => !HasMeta;

        /// <summary>列表列：时间</summary>
        public string CreatedAtLocalDisplay =>
            CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        /// <summary>列表列：大小</summary>
        public string SizeDisplay => FormatBytes(FileSizeBytes);

        /// <summary>列表列：笔记数</summary>
        public string NoteCountLabel =>
            Manifest?.Stats.NoteCount.ToString() ?? "—";

        /// <summary>列表列：触发方式</summary>
        public string TriggerLabel =>
            Manifest != null
                ? LocalDatabaseBackupManifest.GetTriggerLabel(Manifest.Trigger)
                : "旧备份";

        /// <summary>列表列：完整性</summary>
        public string IntegrityLabel
        {
            get
            {
                if (Manifest == null)
                {
                    return IsLegacy ? "未知" : "—";
                }

                return Manifest.IntegrityOk ? "正常" : "异常";
            }
        }

        /// <summary>详情面板文本</summary>
        public string BuildDetailText()
        {
            if (Manifest == null)
            {
                return $"文件：{FileName}\n"
                       + $"时间：{CreatedAtLocalDisplay}\n"
                       + $"大小：{SizeDisplay}\n\n"
                       + "此为旧格式备份（无元数据 sidecar），仅可查看文件信息。\n"
                       + "建议删除后重新「立即备份」以生成带详情的快照。\n\n"
                       + $"路径：{FilePath}";
            }

            var m = Manifest;
            var vacuumLine = m.Vacuum.SourceSizeBeforeBytes > 0
                ? $"源库压缩：{FormatBytes(m.Vacuum.SourceSizeBeforeBytes)}"
                  + $" → {FormatBytes(m.Vacuum.SourceSizeAfterBytes)}\n"
                : string.Empty;
            var integrityLine = m.IntegrityOk
                ? "完整性：通过"
                : "完整性：未通过，不建议用于恢复";

            return $"时间：{m.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n"
                   + $"大小：{FormatBytes(m.FileSizeBytes)}\n"
                   + $"触发：{LocalDatabaseBackupManifest.GetTriggerLabel(m.Trigger)}\n"
                   + $"{integrityLine}\n"
                   + vacuumLine
                   + $"笔记 / 文件夹：{m.Stats.NoteCount} / {m.Stats.FolderCount}\n"
                   + $"软删笔记：{m.Stats.DeletedNoteCount}\n"
                   + $"历史版本：{m.Stats.RevisionCount} 条\n"
                   + $"图片元数据：{m.Stats.ImageMetaCount} 条\n"
                   + $"待同步变更：{m.Stats.PendingChangeCount} 条\n"
                   + $"正文约：{FormatBytes(m.Stats.ContentBytesApprox)}\n"
                   + $"应用版本：{m.AppVersion}\n"
                   + $"路径：{FilePath}";
        }
    }

    /// <summary>枚举指定目录下的备份文件</summary>
    public static List<BackupListItem> ListBackups(string backupDirectory)
    {
        if (!Directory.Exists(backupDirectory))
        {
            return [];
        }

        return Directory.GetFiles(backupDirectory, "local-*.db")
            .Select(path =>
            {
                var info = new FileInfo(path);
                var manifest = LocalDatabaseBackupManifest.TryLoad(path);
                return new BackupListItem
                {
                    FilePath = path,
                    FileName = info.Name,
                    CreatedAtUtc = manifest?.CreatedAtUtc ?? info.LastWriteTimeUtc,
                    FileSizeBytes = info.Length,
                    HasMeta = manifest != null,
                    Manifest = manifest,
                };
            })
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToList();
    }

    /// <summary>删除备份文件及 sidecar</summary>
    public static bool TryDeleteBackup(string backupDbPath, out string error)
    {
        error = string.Empty;
        try
        {
            if (File.Exists(backupDbPath))
            {
                File.Delete(backupDbPath);
            }

            var metaPath = LocalDatabaseBackupManifest.GetMetaPath(backupDbPath);
            if (File.Exists(metaPath))
            {
                File.Delete(metaPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>在资源管理器中定位文件</summary>
    public static void RevealInExplorer(string path)
    {
        if (!File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Path.GetDirectoryName(path) ?? path)
            {
                UseShellExecute = true,
            });
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
        {
            UseShellExecute = true,
        });
    }

    /// <summary>打开备份目录</summary>
    public static void OpenBackupDirectory(string backupDirectory)
    {
        Directory.CreateDirectory(backupDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", backupDirectory)
        {
            UseShellExecute = true,
        });
    }

    /// <summary>格式化字节为人类可读大小</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var mb = bytes / 1024.0 / 1024.0;
        if (mb >= 1024)
        {
            return $"{mb / 1024.0:F2} GB";
        }

        return $"{mb:F2} MB";
    }
}
