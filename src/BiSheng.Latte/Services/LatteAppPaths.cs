using System.IO;

namespace BiSheng.Latte.Services;

/// <summary>
/// Latte 用户数据目录（%LocalAppData%\BiSheng\Latte），与程序安装目录分离，
/// 以便安装版覆盖更新时不丢失笔记与凭据。
/// </summary>
public static class LatteAppPaths
{
    /// <summary>测试专用：覆盖用户数据根目录</summary>
    internal static string? RootOverrideForTests { get; set; }

    /// <summary>用户数据根目录</summary>
    public static string Root =>
        RootOverrideForTests
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BiSheng",
            "Latte");

    /// <summary>运行中的 local.db</summary>
    public static string DatabaseFile => Path.Combine(Root, "local.db");

    /// <summary>同步凭据 config.json</summary>
    public static string ConfigFile => Path.Combine(Root, "config.json");

    /// <summary>笔记图片目录</summary>
    public static string ImagesDirectory => Path.Combine(Root, "images");

    /// <summary>应用日志目录</summary>
    public static string LogDirectory => Path.Combine(Root, "log");

    /// <summary>默认 local.db 备份目录</summary>
    public static string BackupDirectory => Path.Combine(Root, "backups");

    /// <summary>确保用户数据根目录存在</summary>
    public static void EnsureRoot()
    {
        Directory.CreateDirectory(Root);
    }

    /// <summary>规范化路径后比较是否为同一目录</summary>
    public static bool IsSameDirectory(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        var na = Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var nb = Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(na, nb, StringComparison.OrdinalIgnoreCase);
    }
}
