using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BiSheng.Latte.Controls;
using BiSheng.Latte.Models;

namespace BiSheng.Latte.Views;

/// <summary>
/// 外观设置 · 自定义配色：按元数据动态生成「标签 + 色块 + HEX 输入框」行，
/// 色块与 HEX 双向同步，并通过反射读写 <see cref="ThemeDefinition"/> 上的同名颜色属性。
/// </summary>
public partial class AppearanceSettingsWindow
{
    /// <summary>
    /// 界面配色按 UI 区域分组（string 为分组标题，元组为一行颜色），辅助色由 DeriveAuxiliaryColors 自动推导。
    /// SurfaceAlt 同时用于状态栏与导航搜索框；右键菜单复用 Surface / Hover / Danger，无需单独配色。
    /// 推导项：BorderLight, AccentHover, SelectedBorder, ToolbarHover, ToolbarIcon
    /// </summary>
    private static readonly object[] GlobalColorSpec =
    [
        "工具栏",
        ("背景色", "ToolbarBg"),
        ("图标色", "ToolbarIcon"),
        ("文本色", "ToolbarText"),
        "侧边栏",
        ("面板背景", "Surface"),
        ("文本色", "Text"),
        ("悬停背景", "Hover"),
        ("选中底色", "Selected"),
        ("次级背景", "SurfaceAlt"),
        "状态栏",
        ("文本色", "TextMuted"),
        "通用",
        ("背景色", "BgCanvas"),
        ("主文本", "Text"),
        ("次要文本", "TextSecondary"),
        ("强调色", "Accent"),
        ("边框", "Border"),
        ("危险色", "Danger"),
        ("成功色", "Success"),
        "控件",
        ("下拉框背景", "ComboBoxBg"),
        ("Tab 标签背景", "TabBg"),
        ("Tab 选中背景", "TabSelectedBg"),
        ("Tooltip 背景", "TooltipBg"),
        ("Tooltip 文字", "TooltipText"),
    ];

    /// <summary>编辑器配色：Prop 必须与 ThemeDefinition 的属性名一致</summary>
    private static readonly (string Label, string Prop)[] EditorColors =
    [
        ("正文颜色", "NoteText"), ("背景颜色", "NoteBackground"), ("标题颜色", "NoteHeading"),
        ("行内代码背景", "InlineCodeBg"), ("行内代码前景", "InlineCodeFg"),
        ("代码块背景", "CodeBlockBg"), ("代码块边框", "CodeBlockBorder"),
        ("引用边框", "QuoteBorder"), ("引用文字", "QuoteText"), ("引用背景", "QuoteBg"),
        ("链接颜色", "NoteLink"), ("分割线", "NoteHR"), ("列表符号", "NoteBullet"),
        ("光标颜色", "CaretColor"),
    ];

    // ===== 颜色行构建 =====

    private void BuildColorRows()
    {
        GlobalColorPanel.Children.Clear();
        EditorColorPanel.Children.Clear();

        // 界面配色：按分组插入标题行
        foreach (var item in GlobalColorSpec)
        {
            if (item is string sectionTitle)
            {
                GlobalColorPanel.Children.Add(CreateSectionHeader(sectionTitle));
            }
            else if (item is (string label, string prop))
            {
                GlobalColorPanel.Children.Add(CreateColorRow(label, prop));
            }
        }

        foreach (var (label, prop) in EditorColors)
        {
            EditorColorPanel.Children.Add(CreateColorRow(label, prop));
        }
    }

    private UIElement CreateSectionHeader(string title)
    {
        return new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Brush.Accent"),
            FontFamily = (FontFamily)FindResource("UIFont"),
            Margin = new Thickness(0, 10, 0, 4)
        };
    }

    /// <summary>
    /// 生成一行颜色编辑控件。色块 Tag 存属性名；HEX 框 Tag 存 (属性名, 色块)，便于两者互相找到对方。
    /// </summary>
    private UIElement CreateColorRow(string label, string propertyName)
    {
        var hex = GetColorProperty(_editingTheme, propertyName);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        // 标签
        var labelTb = new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("Brush.TextSecondary"),
            FontFamily = (FontFamily)FindResource("UIFont")
        };
        Grid.SetColumn(labelTb, 0);
        grid.Children.Add(labelTb);

        // 色块按钮
        var swatch = new Button
        {
            Style = (Style)FindResource("ColorSwatch"),
            Background = CreateBrush(hex),
            Tag = propertyName
        };
        swatch.Click += OnSwatchClick;
        Grid.SetColumn(swatch, 1);
        grid.Children.Add(swatch);

        // HEX 输入框
        var hexBox = new TextBox
        {
            Text = hex,
            FontFamily = new FontFamily("Consolas, monospace"),
            FontSize = 12,
            Padding = new Thickness(4, 2, 4, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = (propertyName, swatch)
        };
        hexBox.TextChanged += OnHexBoxChanged;
        Grid.SetColumn(hexBox, 3);
        grid.Children.Add(hexBox);

        return grid;
    }

    // ===== 色块点击 → 取色器 =====

    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn)
        {
            return;
        }

        var propertyName = btn.Tag as string ?? "";
        var currentHex = GetColorProperty(_editingTheme, propertyName);

        Color initialColor;
        try
        {
            initialColor = (Color)ColorConverter.ConvertFromString(currentHex);
        }
        catch
        {
            initialColor = Colors.Black;
        }

        var picker = new ColorPickerPopup(initialColor) { Owner = this };
        if (picker.ShowDialog() != true)
        {
            return;
        }

        var newHex = $"#{picker.SelectedColor.R:X2}{picker.SelectedColor.G:X2}{picker.SelectedColor.B:X2}";
        SetColorProperty(_editingTheme, propertyName, newHex);

        // 更新色块
        btn.Background = new SolidColorBrush(picker.SelectedColor);

        // 更新对应的 HEX 输入框（抑制事件，避免回写触发 OnHexBoxChanged）
        foreach (var child in FindSiblingHexBoxes(btn))
        {
            if (child is TextBox tb && tb.Tag is (string p, Button _) && p == propertyName)
            {
                _suppressEvents = true;
                tb.Text = newHex;
                _suppressEvents = false;
            }
        }
    }

    // ===== HEX 输入变更 =====

    private void OnHexBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (sender is not TextBox tb || tb.Tag is not (string prop, Button swatch))
        {
            return;
        }

        var text = tb.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text);
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            SetColorProperty(_editingTheme, prop, hex);
            swatch.Background = new SolidColorBrush(color);
        }
        catch
        {
            // 输入过程中的半截 / 无效颜色直接忽略，等用户输完
        }
    }

    // ===== 反射辅助：读写主题颜色属性 =====

    private static string GetColorProperty(ThemeDefinition theme, string propName)
    {
        var prop = typeof(ThemeDefinition).GetProperty(propName);
        return prop?.GetValue(theme) as string ?? "#000000";
    }

    private static void SetColorProperty(ThemeDefinition theme, string propName, string value)
    {
        var prop = typeof(ThemeDefinition).GetProperty(propName);
        prop?.SetValue(theme, value);
    }

    // ===== UI 辅助 =====

    private static SolidColorBrush CreateBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            return new SolidColorBrush(color);
        }
        catch
        {
            return new SolidColorBrush(Colors.Black);
        }
    }

    /// <summary>遍历色块所在面板中所有颜色行的子控件，供按属性名定位对应的 HEX 输入框</summary>
    private static IEnumerable<UIElement> FindSiblingHexBoxes(Button swatch)
    {
        if (swatch.Parent is not Grid grid || grid.Parent is not StackPanel panel)
        {
            yield break;
        }

        foreach (var child in panel.Children)
        {
            if (child is not Grid g)
            {
                continue;
            }

            foreach (var gc in g.Children)
            {
                if (gc is UIElement uie)
                {
                    yield return uie;
                }
            }
        }
    }
}
