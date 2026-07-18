using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using BiSheng.Editor.Controls.MarkdownEditor.Themes;

namespace BiSheng.Latte.Models;

/// <summary>
/// 统一主题定义：合并全局 UI 颜色 + 编辑器/笔记颜色
/// 内置预设（Light / Dark / Latte / 翰墨书香）为只读，用户主题可自由编辑
/// 持久化为 JSON 文件（hex 字符串格式，如 "#F8F4EF"）
/// </summary>
public class ThemeDefinition
{
    // ===== 元数据 =====

    /// <summary>主题名称（唯一标识，同时用作文件名）</summary>
    public string Name { get; set; } = "未命名";

    /// <summary>主题描述</summary>
    public string Description { get; set; } = "";

    /// <summary>是否为内置预设（内置预设不可编辑）</summary>
    public bool IsBuiltIn { get; set; }

    // ===== 全局 UI 颜色（23 tokens，对应 App.xaml 的 Brush.*）=====

    /// <summary>画布背景</summary>
    public string BgCanvas { get; set; } = "#F8F4EF";

    /// <summary>表面色</summary>
    public string Surface { get; set; } = "#FFFCF8";

    /// <summary>表面辅助色</summary>
    public string SurfaceAlt { get; set; } = "#F2ECE3";

    /// <summary>主文本</summary>
    public string Text { get; set; } = "#3C3228";

    /// <summary>次要文本</summary>
    public string TextSecondary { get; set; } = "#8A7D70";

    /// <summary>弱化文本</summary>
    public string TextMuted { get; set; } = "#B5AA9E";

    /// <summary>边框</summary>
    public string Border { get; set; } = "#DDD4C8";

    /// <summary>浅边框</summary>
    public string BorderLight { get; set; } = "#EAE3D9";

    /// <summary>强调色</summary>
    public string Accent { get; set; } = "#A67C52";

    /// <summary>强调色悬停</summary>
    public string AccentHover { get; set; } = "#8E6840";

    /// <summary>悬停背景</summary>
    public string Hover { get; set; } = "#EDE5D8";

    /// <summary>选中背景</summary>
    public string Selected { get; set; } = "#DDD0BC";

    /// <summary>选中边框</summary>
    public string SelectedBorder { get; set; } = "#C4A67D";

    /// <summary>工具栏背景</summary>
    public string ToolbarBg { get; set; } = "#2C2520";

    /// <summary>工具栏文字</summary>
    public string ToolbarText { get; set; } = "#C8B8A4";

    /// <summary>工具栏图标</summary>
    public string ToolbarIcon { get; set; } = "#D4C4B0";

    /// <summary>工具栏悬停</summary>
    public string ToolbarHover { get; set; } = "#3D342D";

    /// <summary>危险色</summary>
    public string Danger { get; set; } = "#C0392B";

    /// <summary>成功色</summary>
    public string Success { get; set; } = "#6B8E5A";

    /// <summary>下拉框背景（闭合态与下拉面板）</summary>
    public string ComboBoxBg { get; set; } = "#FFFCF8";

    /// <summary>Tab 标签未选中背景</summary>
    public string TabBg { get; set; } = "#F2ECE3";

    /// <summary>Tab 标签选中背景</summary>
    public string TabSelectedBg { get; set; } = "#FFFCF8";

    /// <summary>Tooltip 背景</summary>
    public string TooltipBg { get; set; } = "#2C2520";

    /// <summary>Tooltip 文字</summary>
    public string TooltipText { get; set; } = "#C8B8A4";

    // ===== 编辑器/笔记颜色（13 tokens，对应 MarkdownTheme）=====

    /// <summary>笔记正文颜色</summary>
    public string NoteText { get; set; } = "#3C3228";

    /// <summary>笔记背景色</summary>
    public string NoteBackground { get; set; } = "#FFFCF8";

    /// <summary>标题颜色</summary>
    public string NoteHeading { get; set; } = "#2C2520";

    /// <summary>行内代码背景</summary>
    public string InlineCodeBg { get; set; } = "#F2ECE3";

    /// <summary>行内代码前景</summary>
    public string InlineCodeFg { get; set; } = "#A67C52";

    /// <summary>代码块背景</summary>
    public string CodeBlockBg { get; set; } = "#F8F4EF";

    /// <summary>代码块边框</summary>
    public string CodeBlockBorder { get; set; } = "#DDD4C8";

    /// <summary>引用块边框</summary>
    public string QuoteBorder { get; set; } = "#C4A67D";

    /// <summary>引用块文字</summary>
    public string QuoteText { get; set; } = "#8A7D70";

    /// <summary>引用块背景</summary>
    public string QuoteBg { get; set; } = "#FAF6F1";

    /// <summary>链接颜色</summary>
    public string NoteLink { get; set; } = "#A67C52";

    /// <summary>分割线颜色</summary>
    public string NoteHR { get; set; } = "#DDD4C8";

    /// <summary>列表符号颜色</summary>
    public string NoteBullet { get; set; } = "#A67C52";

    /// <summary>光标颜色</summary>
    public string CaretColor { get; set; } = "#3C3228";

    // ===== 内容字体 =====

    /// <summary>
    /// 编辑器正文字体回退栈（逗号分隔，须为系统已安装字体）。
    /// 解析时由 FontCatalog 自动追加最终保底。
    /// </summary>
    public string ContentFontFamily { get; set; } = FontCatalog.DefaultStack;

    // ===== 内置预设工厂方法 =====

    /// <summary>浅色预设</summary>
    public static ThemeDefinition LightPreset() => new()
    {
        Name = "Light",
        Description = "清新白底，标准配色",
        IsBuiltIn = true,
        BgCanvas = "#F5F5F5",
        Surface = "#FFFFFF",
        SurfaceAlt = "#EEEEEE",
        Text = "#333333",
        TextSecondary = "#777777",
        TextMuted = "#AAAAAA",
        Border = "#DDDDDD",
        BorderLight = "#E8E8E8",
        Accent = "#0078D4",
        AccentHover = "#005A9E",
        Hover = "#F0F0F0",
        Selected = "#CCE4F7",
        SelectedBorder = "#0078D4",
        ToolbarBg = "#2B2B2B",
        ToolbarText = "#CCCCCC",
        ToolbarIcon = "#DDDDDD",
        ToolbarHover = "#3E3E3E",
        Danger = "#E74C3C",
        Success = "#6B8E5A",
        ComboBoxBg = "#FFFFFF",
        TabBg = "#EEEEEE",
        TabSelectedBg = "#FFFFFF",
        TooltipBg = "#2B2B2B",
        TooltipText = "#CCCCCC",
        // 编辑器
        NoteText = "#333333",
        NoteBackground = "#FFFFFF",
        NoteHeading = "#1A1A1A",
        InlineCodeBg = "#F3F3F3",
        InlineCodeFg = "#C7254E",
        CodeBlockBg = "#F8F8F8",
        CodeBlockBorder = "#DCDCDC",
        QuoteBorder = "#C8C8C8",
        QuoteText = "#777777",
        QuoteBg = "#F9F9F9",
        NoteLink = "#007ACC",
        NoteHR = "#C8C8C8",
        NoteBullet = "#646464",
        CaretColor = "#333333",
        ContentFontFamily = FontCatalog.DefaultStack,
    };

    /// <summary>深色预设</summary>
    public static ThemeDefinition DarkPreset() => new()
    {
        Name = "Dark",
        Description = "护眼暗色，减少眩光",
        IsBuiltIn = true,
        BgCanvas = "#1A1A1E",
        Surface = "#222228",
        SurfaceAlt = "#18181C",
        Text = "#E8E6E3",
        TextSecondary = "#A0A0A0",
        TextMuted = "#666666",
        Border = "#3A3A40",
        BorderLight = "#2D2D33",
        Accent = "#A67C52",
        AccentHover = "#C09060",
        Hover = "#2A2A30",
        Selected = "#333340",
        SelectedBorder = "#4A4A5A",
        ToolbarBg = "#0E0E12",
        ToolbarText = "#B0B0B0",
        ToolbarIcon = "#C0C0C0",
        ToolbarHover = "#1E1E24",
        Danger = "#E74C3C",
        Success = "#6B8E5A",
        ComboBoxBg = "#222228",
        TabBg = "#18181C",
        TabSelectedBg = "#222228",
        TooltipBg = "#0E0E12",
        TooltipText = "#B0B0B0",
        // 编辑器
        NoteText = "#DCDCDC",
        NoteBackground = "#1E1E1E",
        NoteHeading = "#F0F0F0",
        InlineCodeBg = "#323232",
        InlineCodeFg = "#E66482",
        CodeBlockBg = "#282828",
        CodeBlockBorder = "#3C3C3C",
        QuoteBorder = "#505050",
        QuoteText = "#AAAAAA",
        QuoteBg = "#232323",
        NoteLink = "#569CD6",
        NoteHR = "#505050",
        NoteBullet = "#969696",
        CaretColor = "#DCDCDC",
        ContentFontFamily = FontCatalog.DefaultStack,
    };

    /// <summary>Latte 默认预设：暖咖啡色调</summary>
    public static ThemeDefinition LattePreset() => new()
    {
        Name = "Latte",
        Description = "暖咖啡色调，温暖优雅",
        IsBuiltIn = true,
        BgCanvas = "#F8F4EF",
        Surface = "#FFFCF8",
        SurfaceAlt = "#F2ECE3",
        Text = "#3C3228",
        TextSecondary = "#8A7D70",
        TextMuted = "#B5AA9E",
        Border = "#DDD4C8",
        BorderLight = "#EAE3D9",
        Accent = "#A67C52",
        AccentHover = "#8E6840",
        Hover = "#EDE5D8",
        Selected = "#DDD0BC",
        SelectedBorder = "#C4A67D",
        ToolbarBg = "#2C2520",
        ToolbarText = "#C8B8A4",
        ToolbarIcon = "#D4C4B0",
        ToolbarHover = "#3D342D",
        Danger = "#C0392B",
        Success = "#6B8E5A",
        ComboBoxBg = "#FFFCF8",
        TabBg = "#F2ECE3",
        TabSelectedBg = "#FFFCF8",
        TooltipBg = "#2C2520",
        TooltipText = "#C8B8A4",
        NoteText = "#3C3228",
        NoteBackground = "#FFFCF8",
        NoteHeading = "#2C2520",
        InlineCodeBg = "#F2ECE3",
        InlineCodeFg = "#A67C52",
        CodeBlockBg = "#F8F4EF",
        CodeBlockBorder = "#DDD4C8",
        QuoteBorder = "#C4A67D",
        QuoteText = "#8A7D70",
        QuoteBg = "#FAF6F1",
        NoteLink = "#A67C52",
        NoteHR = "#DDD4C8",
        NoteBullet = "#A67C52",
        CaretColor = "#3C3228",
        ContentFontFamily = FontCatalog.DefaultStack,
    };

    /// <summary>翰墨书香预设：暖墨纸底、朱砂强调</summary>
    public static ThemeDefinition HanmoShuxiangPreset() => new()
    {
        Name = "翰墨书香",
        Description = "暖墨纸底、朱砂强调，典籍阅读感",
        IsBuiltIn = true,
        BgCanvas = "#FBF7F0",
        Surface = "#FFFEFA",
        SurfaceAlt = "#F5EFE6",
        Text = "#2C1810",
        TextSecondary = "#8C6E5A",
        TextMuted = "#A69485",
        Border = "#D4C5B2",
        BorderLight = "#E8DFD0",
        Accent = "#C43A31",
        AccentHover = "#A53028",
        Hover = "#EDE5D8",
        Selected = "#E8DFD0",
        SelectedBorder = "#C43A31",
        ToolbarBg = "#F0EAE0",
        ToolbarText = "#2C1810",
        ToolbarIcon = "#8C6E5A",
        ToolbarHover = "#E5DDD3",
        Danger = "#B8322A",
        Success = "#5C7A4E",
        ComboBoxBg = "#FFFEFA",
        TabBg = "#F5EFE6",
        TabSelectedBg = "#FFFEFA",
        TooltipBg = "#2C1810",
        TooltipText = "#FFFEFA",
        NoteText = "#2C1810",
        NoteBackground = "#FFFEFA",
        NoteHeading = "#2C1810",
        InlineCodeBg = "#F5EFE6",
        InlineCodeFg = "#C43A31",
        CodeBlockBg = "#F5EFE6",
        CodeBlockBorder = "#D4C5B2",
        QuoteBorder = "#D4C5B2",
        QuoteText = "#8C6E5A",
        QuoteBg = "#F5EFE6",
        NoteLink = "#C43A31",
        NoteHR = "#D4C5B2",
        NoteBullet = "#C43A31",
        CaretColor = "#C43A31",
        ContentFontFamily = FontCatalog.DefaultStack,
    };

    // ===== 转换与应用 =====

    /// <summary>
    /// 将笔记颜色部分转换为 MarkdownTheme（供编辑器使用）
    /// </summary>
    public MarkdownTheme ToMarkdownTheme() => new()
    {
        TextColor = ToBrush(NoteText),
        BackgroundColor = ToBrush(NoteBackground),
        HeadingColor = ToBrush(NoteHeading),
        InlineCodeBackground = ToBrush(InlineCodeBg),
        InlineCodeForeground = ToBrush(InlineCodeFg),
        CodeBlockBackground = ToBrush(CodeBlockBg),
        CodeBlockBorder = ToBrush(CodeBlockBorder),
        QuoteBorderColor = ToBrush(QuoteBorder),
        QuoteTextColor = ToBrush(QuoteText),
        QuoteBackground = ToBrush(QuoteBg),
        LinkColor = ToBrush(NoteLink),
        HorizontalRuleColor = ToBrush(NoteHR),
        BulletColor = ToBrush(NoteBullet),
        CaretColor = ToBrush(CaretColor),
    };

    /// <summary>
    /// 将全局 UI 颜色应用到 App.xaml 资源字典（DynamicResource 自动刷新）
    /// 先自动推导辅助色，再写入所有 Brush
    /// </summary>
    public void ApplyToResources(ResourceDictionary res)
    {
        // 自动推导辅助色（UI 中不再暴露给用户）
        DeriveAuxiliaryColors();

        SetBrush(res, ThemeBrushKeys.BgCanvas, BgCanvas);
        SetBrush(res, ThemeBrushKeys.Surface, Surface);
        SetBrush(res, ThemeBrushKeys.SurfaceAlt, SurfaceAlt);
        SetBrush(res, ThemeBrushKeys.Text, Text);
        SetBrush(res, ThemeBrushKeys.TextSecondary, TextSecondary);
        SetBrush(res, ThemeBrushKeys.TextMuted, TextMuted);
        SetBrush(res, ThemeBrushKeys.Border, Border);
        SetBrush(res, ThemeBrushKeys.BorderLight, BorderLight);
        SetBrush(res, ThemeBrushKeys.Accent, Accent);
        SetBrush(res, ThemeBrushKeys.AccentHover, AccentHover);
        SetBrush(res, ThemeBrushKeys.Hover, Hover);
        SetBrush(res, ThemeBrushKeys.Selected, Selected);
        SetBrush(res, ThemeBrushKeys.SelectedBorder, SelectedBorder);
        SetBrush(res, ThemeBrushKeys.ToolbarBg, ToolbarBg);
        SetBrush(res, ThemeBrushKeys.ToolbarText, ToolbarText);
        SetBrush(res, ThemeBrushKeys.ToolbarIcon, ToolbarIcon);
        SetBrush(res, ThemeBrushKeys.ToolbarHover, ToolbarHover);
        SetBrush(res, ThemeBrushKeys.Danger, Danger);
        SetBrush(res, ThemeBrushKeys.Success, Success);
        SetBrush(res, ThemeBrushKeys.ComboBoxBg, ComboBoxBg);
        SetBrush(res, ThemeBrushKeys.TabBg, TabBg);
        SetBrush(res, ThemeBrushKeys.TabSelectedBg, TabSelectedBg);
        SetBrush(res, ThemeBrushKeys.TooltipBg, TooltipBg);
        SetBrush(res, ThemeBrushKeys.TooltipText, TooltipText);
    }

    /// <summary>
    /// 根据核心色自动推导辅助色（浅色/深色工具栏分别处理）
    /// </summary>
    private void DeriveAuxiliaryColors()
    {
        // BorderLight = Border 向 BgCanvas 淡出
        BorderLight = BlendHex(Border, BgCanvas, 0.55);

        // AccentHover = Accent 加深 15%
        AccentHover = DarkenHex(Accent, 0.15);

        // SelectedBorder = Accent 提亮 10%
        SelectedBorder = LightenHex(Accent, 0.1);

        var toolbarLight = GetLuminance(ToolbarBg) > 0.72;

        // 浅色工具栏悬停略加深，深色工具栏悬停略提亮
        ToolbarHover = toolbarLight
            ? DarkenHex(ToolbarBg, 0.08)
            : LightenHex(ToolbarBg, 0.12);

        // 浅色工具栏图标用次要色；深色工具栏图标略亮于文字
        ToolbarIcon = toolbarLight
            ? TextSecondary
            : LightenHex(ToolbarText, 0.08);
    }

    // ===== JSON 持久化 =====

    /// <summary>
    /// 保存用户主题到 JSON 文件
    /// </summary>
    public void SaveToFile(string directory)
    {
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"{SanitizeFileName(Name)}.json");
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// 从 JSON 文件加载主题
    /// </summary>
    public static ThemeDefinition? LoadFromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<ThemeDefinition>(json);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 创建当前主题的深拷贝（用于"从预设复制"功能）
    /// </summary>
    public ThemeDefinition Clone()
    {
        var json = JsonSerializer.Serialize(this);
        return JsonSerializer.Deserialize<ThemeDefinition>(json)!;
    }

    // ===== 辅助方法 =====

    private static SolidColorBrush ToBrush(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        res[key] = new SolidColorBrush(color);
    }

    // ===== 颜色工具方法 =====

    private static (byte R, byte G, byte B) ParseHex(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return (c.R, c.G, c.B);
    }

    private static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>
    /// 混合两个 hex 颜色，factor 表示 a 的权重（0~1）
    /// </summary>
    private static string BlendHex(string a, string b, double factor)
    {
        var (ar, ag, ab) = ParseHex(a);
        var (br, bg, bb) = ParseHex(b);
        return ToHex(
            (byte)(ar * factor + br * (1 - factor)),
            (byte)(ag * factor + bg * (1 - factor)),
            (byte)(ab * factor + bb * (1 - factor)));
    }

    /// <summary>加深颜色，amount 为加深比例（0~1）</summary>
    private static string DarkenHex(string hex, double amount)
    {
        var (r, g, b) = ParseHex(hex);
        var f = 1 - amount;
        return ToHex((byte)(r * f), (byte)(g * f), (byte)(b * f));
    }

    /// <summary>提亮颜色，amount 为提亮比例（0~1）</summary>
    private static string LightenHex(string hex, double amount)
    {
        var (r, g, b) = ParseHex(hex);
        return ToHex(
            (byte)Math.Min(255, r + (255 - r) * amount),
            (byte)Math.Min(255, g + (255 - g) * amount),
            (byte)Math.Min(255, b + (255 - b) * amount));
    }

    /// <summary>计算相对亮度（0~1）</summary>
    private static double GetLuminance(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
    }
}
