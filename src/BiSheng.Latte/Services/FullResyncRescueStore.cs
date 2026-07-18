using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BiSheng.Latte.Services;

/// <summary>
/// 全量重建抢救快照落盘：清库前写入，成功合并后删除；
/// 中途崩溃可在下次启动时从磁盘恢复未上云变更。
/// </summary>
public static class FullResyncRescueStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>测试可覆盖的文件路径；null 表示使用默认 LocalAppData 路径</summary>
    private static string? _pathOverride;

    /// <summary>默认抢救文件路径</summary>
    public static string DefaultFilePath =>
        Path.Combine(LatteAppPaths.Root, "full-resync-rescue.json");

    /// <summary>当前生效的抢救文件路径</summary>
    public static string FilePath => _pathOverride ?? DefaultFilePath;

    /// <summary>测试用：覆盖抢救文件路径；传 null 恢复默认</summary>
    public static void SetPathOverrideForTests(string? path)
    {
        _pathOverride = path;
    }

    /// <summary>是否存在未完成的全量重建抢救文件</summary>
    public static bool Exists()
    {
        return File.Exists(FilePath);
    }

    /// <summary>将抢救快照原子写入磁盘（先写临时文件再替换）</summary>
    public static void Save(FullResyncRecovery.RescueSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var path = FilePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, json);

        // 目标已存在时用覆盖替换，保证崩溃窗口内始终有完整文件
        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }

    /// <summary>读取磁盘抢救快照；不存在或损坏时返回 null</summary>
    public static FullResyncRecovery.RescueSnapshot? TryLoad()
    {
        var path = FilePath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FullResyncRecovery.RescueSnapshot>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            LogHelper.Warn("读取全量重建抢救文件失败: {0}", ex.Message);
            return null;
        }
    }

    /// <summary>全量重建成功后清除抢救文件</summary>
    public static void Clear()
    {
        var path = FilePath;
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            var tempPath = path + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            LogHelper.Warn("清除全量重建抢救文件失败: {0}", ex.Message);
        }
    }
}
