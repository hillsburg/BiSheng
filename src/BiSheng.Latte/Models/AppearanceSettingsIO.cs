using System.IO;
using System.Text.Json;

namespace BiSheng.Latte.Models;

/// <summary>外观配置导出包：设置 + 可选自定义主题</summary>
public class AppearanceSettingsExport
{
    public int Version { get; set; } = 1;

    public AppearanceSettings Settings { get; set; } = new();

    /// <summary>当 ActiveTheme 为用户自定义主题时一并导出</summary>
    public ThemeDefinition? CustomTheme { get; set; }
}

/// <summary>外观配置导入导出</summary>
public static class AppearanceSettingsIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>导出当前外观设置到 JSON 文件</summary>
    public static void ExportToFile(string path, AppearanceSettings settings)
    {
        var theme = ThemeManager.GetTheme(settings.ActiveTheme);
        var bundle = new AppearanceSettingsExport
        {
            Settings = settings,
            CustomTheme = theme is { IsBuiltIn: false } ? theme.Clone() : null
        };

        var json = JsonSerializer.Serialize(bundle, JsonOptions);
        File.WriteAllText(path, json);
    }

    /// <summary>从 JSON 文件导入外观设置并写入本地配置</summary>
    public static AppearanceSettings ImportFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var bundle = JsonSerializer.Deserialize<AppearanceSettingsExport>(json, JsonOptions)
            ?? throw new InvalidOperationException("无法解析外观配置文件");

        if (bundle.Settings == null)
            throw new InvalidOperationException("外观配置文件缺少 settings 字段");

        if (bundle.CustomTheme != null)
        {
            bundle.CustomTheme.IsBuiltIn = false;
            ThemeManager.SaveUserTheme(bundle.CustomTheme);
            bundle.Settings.ActiveTheme = bundle.CustomTheme.Name;
        }

        bundle.Settings.Save();
        return bundle.Settings;
    }
}
