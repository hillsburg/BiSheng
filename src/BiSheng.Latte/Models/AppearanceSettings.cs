using System.IO;
using System.Text.Json;

namespace BiSheng.Latte.Models;

/// <summary>导航区布局模式</summary>
public enum NavigationLayoutMode
{
    /// <summary>文件夹树与笔记列表左右并列</summary>
    SideBySide,

    /// <summary>笔记作为叶节点归纳到文件夹树内</summary>
    TreeView
}

/// <summary>工具栏位置</summary>
public enum ToolbarPlacement
{
    /// <summary>窗口顶部横条</summary>
    Top,

    /// <summary>导航区左侧竖条</summary>
    NavLeft
}

/// <summary>工具栏显示方式</summary>
public enum ToolbarVisibilityMode
{
    /// <summary>始终可见</summary>
    Fixed,

    /// <summary>默认隐藏，鼠标悬停时淡入</summary>
    AutoHide
}

/// <summary>状态栏显示方式</summary>
public enum StatusBarVisibilityMode
{
    /// <summary>始终可见</summary>
    Fixed,

    /// <summary>完全隐藏</summary>
    Hidden
}

/// <summary>
/// 外观设置模型：字体、行高、标题字号、主题
/// 持久化为 JSON 文件
/// </summary>
public class AppearanceSettings
{
    // ===== 笔记样式 =====

    /// <summary>行高倍数（默认 1.5）</summary>
    public double LineSpacing { get; set; } = 1.5;

    /// <summary>一级标题字号</summary>
    public double H1Size { get; set; } = 28;

    /// <summary>二级标题字号</summary>
    public double H2Size { get; set; } = 24;

    /// <summary>三级标题字号</summary>
    public double H3Size { get; set; } = 20;

    /// <summary>四级标题字号</summary>
    public double H4Size { get; set; } = 18;

    /// <summary>五级标题字号</summary>
    public double H5Size { get; set; } = 16;

    /// <summary>六级标题字号</summary>
    public double H6Size { get; set; } = 14;

    /// <summary>正文字号</summary>
    public double BodySize { get; set; } = 15;

    /// <summary>
    /// 各主题的编辑器字体覆盖（主题名 → 字体回退栈）。
    /// 未收录时用 ThemeDefinition.ContentFontFamily 预设。
    /// </summary>
    public Dictionary<string, string> ThemeContentFonts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    // ===== 导航区布局 =====

    /// <summary>导航区布局模式：并列 / 归纳</summary>
    public NavigationLayoutMode LayoutMode { get; set; } = NavigationLayoutMode.SideBySide;

    /// <summary>工具栏位置：顶部 / 导航左侧</summary>
    public ToolbarPlacement ToolbarPlacement { get; set; } = ToolbarPlacement.Top;

    /// <summary>工具栏显示：固定 / 悬停淡入淡出（两种位置均生效）</summary>
    public ToolbarVisibilityMode ToolbarVisibilityMode { get; set; } = ToolbarVisibilityMode.AutoHide;

    /// <summary>状态栏显示：固定 / 隐藏</summary>
    public StatusBarVisibilityMode StatusBarVisibilityMode { get; set; } = StatusBarVisibilityMode.Fixed;

    /// <summary>关闭主窗口时最小化到系统托盘（托盘菜单可退出）</summary>
    public bool CloseToTray { get; set; } = true;

    // ===== 主题 =====

    /// <summary>活动主题名称：System（跟随系统）/ Light / Dark / Latte / 用户自定义主题名</summary>
    public string ActiveTheme { get; set; } = "Latte";

    /// <summary>读取主题编辑器字体覆盖；无覆盖返回 null</summary>
    public string? TryGetThemeContentFont(string themeName)
    {
        EnsureThemeContentFonts();
        if (string.IsNullOrWhiteSpace(themeName))
        {
            return null;
        }

        return ThemeContentFonts.TryGetValue(themeName, out var font) && !string.IsNullOrWhiteSpace(font)
            ? font
            : null;
    }

    /// <summary>写入主题编辑器字体覆盖</summary>
    public void SetThemeContentFont(string themeName, string fontStack)
    {
        EnsureThemeContentFonts();
        if (string.IsNullOrWhiteSpace(themeName) || string.IsNullOrWhiteSpace(fontStack))
        {
            return;
        }

        ThemeContentFonts[themeName] = fontStack;
    }

    /// <summary>保证字典非空且键比较忽略大小写</summary>
    private void EnsureThemeContentFonts()
    {
        if (ThemeContentFonts == null)
        {
            ThemeContentFonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        if (!Equals(ThemeContentFonts.Comparer, StringComparer.OrdinalIgnoreCase))
        {
            ThemeContentFonts = new Dictionary<string, string>(ThemeContentFonts, StringComparer.OrdinalIgnoreCase);
        }
    }

    // ===== 持久化 =====

    private static string SettingsPath =>
        Path.Combine(Services.LatteAppPaths.Root, "appearance.json");

    /// <summary>
    /// 从磁盘加载设置（文件不存在则返回默认值）
    /// </summary>
    public static AppearanceSettings Load()
    {
        try
        {
            var path = SettingsPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppearanceSettings>(json) ?? new AppearanceSettings();
                settings.EnsureThemeContentFonts();
                return settings;
            }
        }
        catch { /* 解析失败则返回默认值 */ }
        return new AppearanceSettings();
    }

    /// <summary>
    /// 保存设置到磁盘
    /// </summary>
    public void Save()
    {
        var path = SettingsPath;
        var dir = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
