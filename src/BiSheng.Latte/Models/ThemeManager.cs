using System.IO;
using Microsoft.Win32;

namespace BiSheng.Latte.Models;

/// <summary>
/// 主题管理器：负责内置预设和用户自定义主题的加载、保存、解析
/// </summary>
public static class ThemeManager
{
    /// <summary>用户主题存储目录</summary>
    private static readonly string UserThemesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BiSheng", "Latte", "themes");

    /// <summary>
    /// 获取所有内置预设
    /// </summary>
    public static List<ThemeDefinition> GetBuiltInThemes() =>
    [
        ThemeDefinition.LightPreset(),
        ThemeDefinition.DarkPreset(),
        ThemeDefinition.LattePreset(),
        ThemeDefinition.HanmoShuxiangPreset(),
    ];

    /// <summary>
    /// 从磁盘加载所有用户自定义主题
    /// </summary>
    public static List<ThemeDefinition> LoadUserThemes()
    {
        var themes = new List<ThemeDefinition>();
        if (!Directory.Exists(UserThemesDir))
            return themes;

        foreach (var file in Directory.GetFiles(UserThemesDir, "*.json"))
        {
            var theme = ThemeDefinition.LoadFromFile(file);
            if (theme != null)
            {
                theme.IsBuiltIn = false;
                themes.Add(theme);
            }
        }

        return themes.OrderBy(t => t.Name).ToList();
    }

    /// <summary>
    /// 获取全部主题（内置 + 用户）
    /// </summary>
    public static List<ThemeDefinition> GetAllThemes()
    {
        var all = GetBuiltInThemes();
        all.AddRange(LoadUserThemes());
        return all;
    }

    /// <summary>
    /// 按名称查找主题（不区分大小写）
    /// </summary>
    public static ThemeDefinition? GetTheme(string name)
    {
        return GetAllThemes().FirstOrDefault(t =>
            t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 保存用户主题到磁盘（新建或覆盖）
    /// </summary>
    public static void SaveUserTheme(ThemeDefinition theme)
    {
        theme.IsBuiltIn = false;
        theme.SaveToFile(UserThemesDir);
    }

    /// <summary>
    /// 删除用户主题文件
    /// </summary>
    public static bool DeleteUserTheme(string name)
    {
        var sanitized = string.Join("_",
            name.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var path = Path.Combine(UserThemesDir, $"{sanitized}.json");
        if (File.Exists(path))
        {
            File.Delete(path);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 根据设置解析当前应使用的主题（处理"跟随系统"模式）
    /// </summary>
    public static ThemeDefinition Resolve(AppearanceSettings settings)
    {
        var themeName = settings.ActiveTheme;

        // "跟随系统" → 自动选择 Light 或 Dark
        if (themeName == "System")
            themeName = IsSystemDarkMode() ? "Dark" : "Light";

        // 尝试查找主题
        var theme = GetTheme(themeName);

        // 找不到则回退到 Latte
        return theme ?? ThemeDefinition.LattePreset();
    }

    /// <summary>
    /// 检测 Windows 是否处于深色模式
    /// </summary>
    private static bool IsSystemDarkMode()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}
