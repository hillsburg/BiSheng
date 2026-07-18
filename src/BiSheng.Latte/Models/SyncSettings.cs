using System.IO;
using System.Text.Json;

namespace BiSheng.Latte.Models;

/// <summary>
/// 同步行为配置：周期 Push、退出 flush、图片轮询等
/// 持久化为 JSON，可在工具栏「同步设置」对话框中调整
/// </summary>
public class SyncSettings
{
    /// <summary>周期 Push 间隔（秒）；仅有待推送变更时执行</summary>
    public int PeriodicPushIntervalSeconds { get; set; } = 30;

    /// <summary>关闭应用前是否 flush 待推送队列</summary>
    public bool FlushOnExit { get; set; } = true;

    /// <summary>窗口重新激活时是否触发 Push + Pull</summary>
    public bool SyncOnAppActivated { get; set; } = true;

    /// <summary>网络或 SignalR 恢复时是否触发 Push + Pull</summary>
    public bool SyncOnNetworkRecover { get; set; } = true;

    /// <summary>SignalR 已连接且无 pending 时，是否周期探测服务端版本（补偿静默丢失推送）</summary>
    public bool PeriodicVersionProbeWhenConnected { get; set; } = true;

    /// <summary>图片上传轮询间隔（秒）</summary>
    public int ImageUploadIntervalSeconds { get; set; } = 10;

    /// <summary>图片增量拉取间隔（秒）</summary>
    public int ImagePullIntervalSeconds { get; set; } = 60;

    /// <summary>周期 Push 间隔（毫秒）</summary>
    public int PeriodicPushIntervalMs => PeriodicPushIntervalSeconds * 1000;

    /// <summary>图片上传轮询间隔（毫秒）</summary>
    public int ImageUploadIntervalMs => ImageUploadIntervalSeconds * 1000;

    /// <summary>图片增量拉取间隔（毫秒）</summary>
    public int ImagePullIntervalMs => ImagePullIntervalSeconds * 1000;

    private static string SettingsPath =>
        Path.Combine(Services.LatteAppPaths.Root, "sync.json");

    /// <summary>从磁盘加载；文件不存在或损坏时返回默认值</summary>
    public static SyncSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<SyncSettings>(json) ?? new SyncSettings();
                settings.Normalize();
                return settings;
            }
        }
        catch
        {
            /* 解析失败则返回默认值 */
        }

        var defaults = new SyncSettings();
        defaults.Normalize();
        return defaults;
    }

    /// <summary>保存到磁盘</summary>
    public void Save()
    {
        Normalize();

        var path = SettingsPath;
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>将数值限制在合理范围内</summary>
    public void Normalize()
    {
        PeriodicPushIntervalSeconds = Clamp(PeriodicPushIntervalSeconds, 5, 600);
        ImageUploadIntervalSeconds = Clamp(ImageUploadIntervalSeconds, 5, 600);
        ImagePullIntervalSeconds = Clamp(ImagePullIntervalSeconds, 10, 3600);
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(value, max));
    }
}
