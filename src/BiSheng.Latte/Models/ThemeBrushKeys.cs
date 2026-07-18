namespace BiSheng.Latte.Models;

/// <summary>
/// 主题画刷资源键（与 App.xaml / ThemeDefinition.ApplyToResources 一致）
/// C# 侧通过本类引用，避免魔法字符串；XAML 仍使用同名 x:Key
/// </summary>
public static class ThemeBrushKeys
{
    /// <summary>画布背景</summary>
    public const string BgCanvas = "Brush.BgCanvas";

    /// <summary>表面色</summary>
    public const string Surface = "Brush.Surface";

    /// <summary>表面辅助色</summary>
    public const string SurfaceAlt = "Brush.SurfaceAlt";

    /// <summary>主文本</summary>
    public const string Text = "Brush.Text";

    /// <summary>次要文本</summary>
    public const string TextSecondary = "Brush.TextSecondary";

    /// <summary>弱化文本</summary>
    public const string TextMuted = "Brush.TextMuted";

    /// <summary>边框</summary>
    public const string Border = "Brush.Border";

    /// <summary>浅边框</summary>
    public const string BorderLight = "Brush.BorderLight";

    /// <summary>强调色</summary>
    public const string Accent = "Brush.Accent";

    /// <summary>强调色悬停</summary>
    public const string AccentHover = "Brush.AccentHover";

    /// <summary>悬停背景</summary>
    public const string Hover = "Brush.Hover";

    /// <summary>选中背景</summary>
    public const string Selected = "Brush.Selected";

    /// <summary>选中边框</summary>
    public const string SelectedBorder = "Brush.SelectedBorder";

    /// <summary>工具栏背景</summary>
    public const string ToolbarBg = "Brush.ToolbarBg";

    /// <summary>工具栏文字</summary>
    public const string ToolbarText = "Brush.ToolbarText";

    /// <summary>工具栏图标</summary>
    public const string ToolbarIcon = "Brush.ToolbarIcon";

    /// <summary>工具栏悬停</summary>
    public const string ToolbarHover = "Brush.ToolbarHover";

    /// <summary>危险色</summary>
    public const string Danger = "Brush.Danger";

    /// <summary>成功色</summary>
    public const string Success = "Brush.Success";

    /// <summary>下拉框背景</summary>
    public const string ComboBoxBg = "Brush.ComboBoxBg";

    /// <summary>Tab 标签背景</summary>
    public const string TabBg = "Brush.TabBg";

    /// <summary>Tab 选中背景</summary>
    public const string TabSelectedBg = "Brush.TabSelectedBg";

    /// <summary>Tooltip 背景</summary>
    public const string TooltipBg = "Brush.TooltipBg";

    /// <summary>Tooltip 文字</summary>
    public const string TooltipText = "Brush.TooltipText";
}
