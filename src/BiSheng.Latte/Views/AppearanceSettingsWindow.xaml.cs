using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using BiSheng.Latte.Controls;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using Microsoft.Win32;

namespace BiSheng.Latte.Views;

/// <summary>
/// 外观与样式设置弹窗：统一主题选择 + 自定义颜色编辑
/// </summary>
public partial class AppearanceSettingsWindow : Window
{
    private readonly AppearanceSettings _settings;

    /// <summary>
    /// 应用回调：传入当前内存中的设置以便实时预览（不必先落盘）
    /// </summary>
    public Action<AppearanceSettings>? ApplyCallback { get; set; }

    /// <summary>当前正在编辑的主题（可能是内置预设或用户主题的副本）</summary>
    private ThemeDefinition _editingTheme = null!;

    /// <summary>ComboBox 中的选项列表</summary>
    private readonly List<ThemeOption> _options = new();

    /// <summary>是否正在程序化更新 UI（防止事件循环）</summary>
    private bool _suppressEvents;

    // ===== 颜色属性元数据 =====

    /// <summary>
    /// 界面配色按 UI 区域分组，辅助色由 DeriveAuxiliaryColors 自动推导。
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

    // 弹出菜单（导航右键等）复用：Surface 底板、Hover 悬停、Text 文字、Danger 危险项

    private static readonly (string Label, string Prop)[] EditorColors =
    [
        ("正文颜色", "NoteText"), ("背景颜色", "NoteBackground"), ("标题颜色", "NoteHeading"),
        ("行内代码背景", "InlineCodeBg"), ("行内代码前景", "InlineCodeFg"),
        ("代码块背景", "CodeBlockBg"), ("代码块边框", "CodeBlockBorder"),
        ("引用边框", "QuoteBorder"), ("引用文字", "QuoteText"), ("引用背景", "QuoteBg"),
        ("链接颜色", "NoteLink"), ("分割线", "NoteHR"), ("列表符号", "NoteBullet"),
        ("光标颜色", "CaretColor"),
    ];

    public AppearanceSettingsWindow()
    {
        InitializeComponent();
        _settings = AppearanceSettings.Load();
        LoadCurrentSettings();
    }

    // ===== 布局模式选择 =====

    private void OnSideBySideSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.LayoutMode = NavigationLayoutMode.SideBySide;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnTreeViewSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.LayoutMode = NavigationLayoutMode.TreeView;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarTopSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarPlacement = ToolbarPlacement.Top;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarNavLeftSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarPlacement = ToolbarPlacement.NavLeft;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarFixedSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarVisibilityMode = ToolbarVisibilityMode.Fixed;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnToolbarAutoHideSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.ToolbarVisibilityMode = ToolbarVisibilityMode.AutoHide;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnStatusBarFixedSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.StatusBarVisibilityMode = StatusBarVisibilityMode.Fixed;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnStatusBarHiddenSelected(object sender, MouseButtonEventArgs e)
    {
        _settings.StatusBarVisibilityMode = StatusBarVisibilityMode.Hidden;
        UpdateLayoutCards();
        ApplyCallback?.Invoke(_settings);
    }

    private void OnCloseToTrayChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _settings.CloseToTray = CloseToTrayBox.IsChecked == true;
        ApplyCallback?.Invoke(_settings);
    }

    private void UpdateLayoutCards()
    {
        var accentBrush = (Brush)FindResource("Brush.Accent");
        var borderLightBrush = (Brush)FindResource("Brush.BorderLight");

        if (_settings.LayoutMode == NavigationLayoutMode.SideBySide)
        {
            SideBySideCard.BorderBrush = accentBrush;
            TreeViewCard.BorderBrush = borderLightBrush;
        }
        else
        {
            SideBySideCard.BorderBrush = borderLightBrush;
            TreeViewCard.BorderBrush = accentBrush;
        }

        if (_settings.ToolbarPlacement == ToolbarPlacement.Top)
        {
            ToolbarTopCard.BorderBrush = accentBrush;
            ToolbarNavLeftCard.BorderBrush = borderLightBrush;
        }
        else
        {
            ToolbarTopCard.BorderBrush = borderLightBrush;
            ToolbarNavLeftCard.BorderBrush = accentBrush;
        }

        if (_settings.ToolbarVisibilityMode == ToolbarVisibilityMode.Fixed)
        {
            ToolbarFixedCard.BorderBrush = accentBrush;
            ToolbarAutoHideCard.BorderBrush = borderLightBrush;
        }
        else
        {
            ToolbarFixedCard.BorderBrush = borderLightBrush;
            ToolbarAutoHideCard.BorderBrush = accentBrush;
        }

        if (_settings.StatusBarVisibilityMode == StatusBarVisibilityMode.Fixed)
        {
            StatusBarFixedCard.BorderBrush = accentBrush;
            StatusBarHiddenCard.BorderBrush = borderLightBrush;
        }
        else
        {
            StatusBarFixedCard.BorderBrush = borderLightBrush;
            StatusBarHiddenCard.BorderBrush = accentBrush;
        }
    }

    // ===== 初始化 =====

    private void LoadCurrentSettings()
    {
        // 字体列表：系统已安装字体（按主题分别保存）
        var fonts = FontCatalog.BuildPickerItems();
        FontFamilyBox.ItemsSource = fonts;
        FontFamilyBox.DisplayMemberPath = nameof(FontPickItem.Display);

        // 滑块
        LineSpacingSlider.Value = _settings.LineSpacing;
        BodySizeSlider.Value = _settings.BodySize;
        H1SizeSlider.Value = _settings.H1Size;
        H2SizeSlider.Value = _settings.H2Size;
        H3SizeSlider.Value = _settings.H3Size;
        H4SizeSlider.Value = _settings.H4Size;
        H5SizeSlider.Value = _settings.H5Size;
        H6SizeSlider.Value = _settings.H6Size;
        UpdateValueLabels();

        // 布局模式卡片
        UpdateLayoutCards();

        // 填充主题下拉框
        RefreshThemeOptions();

        // 选中当前主题 / 托盘选项（程序化赋值时抑制事件）
        _suppressEvents = true;
        CloseToTrayBox.IsChecked = _settings.CloseToTray;
        var currentIdx = _options.FindIndex(o => o.Value == _settings.ActiveTheme);
        ThemeBox.SelectedIndex = currentIdx >= 0 ? currentIdx : 0;
        _suppressEvents = false;

        // 加载编辑主题
        _editingTheme = ResolveEditingTheme();
        UpdateEditorVisibility();
        SyncFontBoxFromEditingTheme();
    }

    private void RefreshThemeOptions()
    {
        _options.Clear();
        _options.Add(new ThemeOption("跟随系统", "System", false));

        foreach (var t in ThemeManager.GetAllThemes())
            _options.Add(new ThemeOption(t.Name, t.Name, !t.IsBuiltIn));

        ThemeBox.ItemsSource = _options;
        ThemeBox.DisplayMemberPath = "Display";
    }

    private ThemeDefinition ResolveEditingTheme()
    {
        var themeName = _settings.ActiveTheme;
        if (themeName == "System")
        {
            themeName = "Latte";
        }

        var theme = ThemeManager.GetTheme(themeName)?.Clone() ?? ThemeDefinition.LattePreset().Clone();
        ApplyStoredFontOntoTheme(theme);
        return theme;
    }

    /// <summary>把外观设置里按主题保存的字体覆盖到主题对象上</summary>
    private void ApplyStoredFontOntoTheme(ThemeDefinition theme)
    {
        var preferred = FontCatalog.ResolvePreferredStack(theme, _settings);
        theme.ContentFontFamily = preferred;
    }

    /// <summary>当前用于存取字体覆盖的主题名（跟随系统时用解析后的实题）</summary>
    private string CurrentFontThemeKey()
    {
        if (_settings.ActiveTheme == "System")
        {
            return ThemeManager.Resolve(_settings).Name;
        }

        return string.IsNullOrWhiteSpace(_editingTheme?.Name)
            ? _settings.ActiveTheme
            : _editingTheme.Name;
    }

    // ===== 主题选择 =====

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ThemeBox.SelectedIndex < 0) return;

        // 离开当前主题前：把字体选项写入该主题的保存位
        CollectCurrentSettings();
        if (!_editingTheme.IsBuiltIn)
        {
            ThemeManager.SaveUserTheme(_editingTheme);
        }

        var option = _options[ThemeBox.SelectedIndex];
        _settings.ActiveTheme = option.Value;
        _editingTheme = ResolveEditingTheme();
        UpdateEditorVisibility();
        SyncFontBoxFromEditingTheme();

        // 切换主题立即预览配色与编辑器字体
        ApplyCallback?.Invoke(_settings);
    }

    private void UpdateEditorVisibility()
    {
        var isCustom = !_editingTheme.IsBuiltIn;
        CustomNamePanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        GlobalColorsExpander.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        EditorColorsExpander.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
        CreateCustomBtn.Visibility = isCustom ? Visibility.Collapsed : Visibility.Visible;

        if (isCustom)
        {
            _suppressEvents = true;
            ThemeNameBox.Text = _editingTheme.Name;
            _suppressEvents = false;
            BuildColorRows();
        }
    }

    // ===== 颜色行构建 =====

    private void BuildColorRows()
    {
        GlobalColorPanel.Children.Clear();
        EditorColorPanel.Children.Clear();

        // 界面配色：按分组插入标题行
        foreach (var item in GlobalColorSpec)
        {
            if (item is string sectionTitle)
                GlobalColorPanel.Children.Add(CreateSectionHeader(sectionTitle));
            else if (item is (string label, string prop))
                GlobalColorPanel.Children.Add(CreateColorRow(label, prop));
        }

        foreach (var (label, prop) in EditorColors)
            EditorColorPanel.Children.Add(CreateColorRow(label, prop));
    }

    private UIElement CreateSectionHeader(string title)
    {
        var tb = new TextBlock
        {
            Text = title,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Brush.Accent"),
            FontFamily = (FontFamily)FindResource("UIFont"),
            Margin = new Thickness(0, 10, 0, 4)
        };
        return tb;
    }

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
            Text = label, FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
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
            FontSize = 12, Padding = new Thickness(4, 2, 4, 2),
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
        if (sender is not Button btn) return;
        var propertyName = btn.Tag as string ?? "";
        var currentHex = GetColorProperty(_editingTheme, propertyName);

        Color initialColor;
        try { initialColor = (Color)ColorConverter.ConvertFromString(currentHex); }
        catch { initialColor = Colors.Black; }

        var picker = new ColorPickerPopup(initialColor) { Owner = this };
        if (picker.ShowDialog() == true)
        {
            var newHex = $"#{picker.SelectedColor.R:X2}{picker.SelectedColor.G:X2}{picker.SelectedColor.B:X2}";
            SetColorProperty(_editingTheme, propertyName, newHex);

            // 更新色块
            btn.Background = new SolidColorBrush(picker.SelectedColor);

            // 更新对应的 HEX 输入框
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
    }

    // ===== HEX 输入变更 =====

    private void OnHexBoxChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (sender is not TextBox tb || tb.Tag is not (string prop, Button swatch)) return;

        var text = tb.Text.Trim();
        if (!text.StartsWith('#')) text = "#" + text;

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text);
            var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            SetColorProperty(_editingTheme, prop, hex);
            swatch.Background = new SolidColorBrush(color);
        }
        catch { /* 无效输入，忽略 */ }
    }

    // ===== 主题名称变更 =====

    private void OnThemeNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents) return;
        if (_editingTheme.IsBuiltIn) return;

        var oldName = _editingTheme.Name;
        _editingTheme.Name = ThemeNameBox.Text.Trim();

        // 更新下拉框显示
        var option = _options.FirstOrDefault(o => o.Value == oldName);
        if (option != null)
        {
            option.Value = _editingTheme.Name;
            option.Display = $"{_editingTheme.Name} (自定义)";
            _settings.ActiveTheme = _editingTheme.Name;
            ThemeBox.Items.Refresh();
        }
    }

    // ===== 复制并自定义 =====

    private void OnCreateCustom(object sender, RoutedEventArgs e)
    {
        var baseTheme = ResolveEditingTheme();
        var custom = baseTheme.Clone();
        custom.IsBuiltIn = false;
        custom.Name = $"{baseTheme.Name} 自定义";
        custom.Description = $"基于 {baseTheme.Name} 创建的自定义主题";

        // 保存用户主题
        ThemeManager.SaveUserTheme(custom);

        // 刷新下拉框并选中
        RefreshThemeOptions();
        _suppressEvents = true;
        var idx = _options.FindIndex(o => o.Value == custom.Name);
        ThemeBox.SelectedIndex = idx >= 0 ? idx : ThemeBox.Items.Count - 1;
        _suppressEvents = false;

        _settings.ActiveTheme = custom.Name;
        _editingTheme = custom.Clone();
        UpdateEditorVisibility();
        SyncFontBoxFromEditingTheme();
        ApplyCallback?.Invoke(_settings);
    }

    // ===== 删除自定义主题 =====

    private void OnDeleteTheme(object sender, RoutedEventArgs e)
    {
        if (_editingTheme.IsBuiltIn) return;

        if (!AppDialog.ConfirmDanger($"确认删除主题【{_editingTheme.Name}】？", "确认删除")) return;

        ThemeManager.DeleteUserTheme(_editingTheme.Name);

        // 回退到 Latte
        _settings.ActiveTheme = "Latte";
        RefreshThemeOptions();

        _suppressEvents = true;
        var idx = _options.FindIndex(o => o.Value == "Latte");
        ThemeBox.SelectedIndex = idx >= 0 ? idx : 0;
        _suppressEvents = false;

        _editingTheme = ThemeDefinition.LattePreset().Clone();
        UpdateEditorVisibility();
        SyncFontBoxFromEditingTheme();
        ApplyCallback?.Invoke(_settings);
    }

    // ===== 保存 / 取消 =====

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CollectCurrentSettings();

        // 保存自定义主题
        if (!_editingTheme.IsBuiltIn)
            ThemeManager.SaveUserTheme(_editingTheme);

        _settings.Save();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnExport(object sender, RoutedEventArgs e)
    {
        CollectCurrentSettings();

        var dialog = new SaveFileDialog
        {
            Title = "导出外观配置",
            Filter = "外观配置 (*.json)|*.json",
            FileName = "bisheng-appearance.json",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            if (!_editingTheme.IsBuiltIn)
                ThemeManager.SaveUserTheme(_editingTheme);

            AppearanceSettingsIO.ExportToFile(dialog.FileName, _settings);
            AppDialog.Success("外观配置已导出。", "导出成功");
        }
        catch (Exception ex)
        {
            AppDialog.Error($"导出失败：{ex.Message}", "导出失败");
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入外观配置",
            Filter = "外观配置 (*.json)|*.json",
            DefaultExt = ".json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var imported = AppearanceSettingsIO.ImportFromFile(dialog.FileName);
            _settings.LineSpacing = imported.LineSpacing;
            _settings.BodySize = imported.BodySize;
            _settings.H1Size = imported.H1Size;
            _settings.H2Size = imported.H2Size;
            _settings.H3Size = imported.H3Size;
            _settings.H4Size = imported.H4Size;
            _settings.H5Size = imported.H5Size;
            _settings.H6Size = imported.H6Size;
            _settings.LayoutMode = imported.LayoutMode;
            _settings.ToolbarPlacement = imported.ToolbarPlacement;
            _settings.ToolbarVisibilityMode = imported.ToolbarVisibilityMode;
            _settings.StatusBarVisibilityMode = imported.StatusBarVisibilityMode;
            _settings.CloseToTray = imported.CloseToTray;
            _settings.ActiveTheme = imported.ActiveTheme;
            _settings.ThemeContentFonts = new Dictionary<string, string>(
                imported.ThemeContentFonts ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);

            LoadCurrentSettings();
            ApplyCallback?.Invoke(_settings);
            AppDialog.Success("外观配置已导入并应用。", "导入成功");
        }
        catch (Exception ex)
        {
            AppDialog.Error($"导入失败：{ex.Message}", "导入失败");
        }
    }

    private void CollectCurrentSettings()
    {
        // 字体写入「当前主题」的覆盖表；自定义主题同时更新主题文件字段
        if (FontFamilyBox.SelectedItem is FontPickItem selectedFont)
        {
            _editingTheme.ContentFontFamily = selectedFont.Source;
            _settings.SetThemeContentFont(CurrentFontThemeKey(), selectedFont.Source);
        }

        _settings.LineSpacing = LineSpacingSlider.Value;
        _settings.BodySize = BodySizeSlider.Value;
        _settings.H1Size = H1SizeSlider.Value;
        _settings.H2Size = H2SizeSlider.Value;
        _settings.H3Size = H3SizeSlider.Value;
        _settings.H4Size = H4SizeSlider.Value;
        _settings.H5Size = H5SizeSlider.Value;
        _settings.H6Size = H6SizeSlider.Value;
        _settings.CloseToTray = CloseToTrayBox.IsChecked == true;
    }

    /// <summary>改字体：保存到当前主题并即时预览</summary>
    private void OnFontFamilySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (FontFamilyBox.SelectedItem is FontPickItem item)
        {
            _editingTheme.ContentFontFamily = item.Source;
            _settings.SetThemeContentFont(CurrentFontThemeKey(), item.Source);
        }

        UpdateFontAvailabilityHint();
        CollectCurrentSettings();

        if (!_editingTheme.IsBuiltIn)
        {
            ThemeManager.SaveUserTheme(_editingTheme);
        }

        ApplyCallback?.Invoke(_settings);
    }

    /// <summary>将字体下拉同步为当前主题已保存的编辑器字体</summary>
    private void SyncFontBoxFromEditingTheme()
    {
        var themeForFont = _settings.ActiveTheme == "System"
            ? ThemeManager.Resolve(_settings)
            : _editingTheme;
        ApplyStoredFontOntoTheme(themeForFont);

        var fonts = FontFamilyBox.ItemsSource as IList<FontPickItem>
            ?? FontCatalog.BuildPickerItems();
        if (FontFamilyBox.ItemsSource == null)
        {
            FontFamilyBox.ItemsSource = fonts;
            FontFamilyBox.DisplayMemberPath = nameof(FontPickItem.Display);
        }

        var preferred = FontCatalog.ResolvePreferredStack(themeForFont, _settings);
        _suppressEvents = true;
        FontFamilyBox.IsEnabled = true;
        FontFamilyBox.SelectedItem =
            FontCatalog.FindMatchingItem(fonts, preferred) ?? fonts[0];
        _suppressEvents = false;

        FontThemeHint.Text =
            $"主题「{themeForFont.Name}」的编辑器字体：{FontCatalog.DescribeStack(preferred)}";
        UpdateFontAvailabilityHint();
    }

    /// <summary>所选字体不可用时给出回退提示</summary>
    private void UpdateFontAvailabilityHint()
    {
        if (FontFamilyBox.SelectedItem is not FontPickItem item)
        {
            FontAvailabilityHint.Visibility = Visibility.Collapsed;
            return;
        }

        if (item.IsAvailable || FontCatalog.IsPreferredAvailable(item.Source))
        {
            FontAvailabilityHint.Visibility = Visibility.Collapsed;
            return;
        }

        FontAvailabilityHint.Text =
            $"「{FontCatalog.DescribeStack(item.Source)}」当前不可用，将回退到微软雅黑 / Segoe UI 等保底字体。";
        FontAvailabilityHint.Visibility = Visibility.Visible;
    }

    private void OnApply(object sender, RoutedEventArgs e)
    {
        CollectCurrentSettings();

        if (!_editingTheme.IsBuiltIn)
            ThemeManager.SaveUserTheme(_editingTheme);

        _settings.Save();

        // 通知宿主窗口重新应用外观
        ApplyCallback?.Invoke(_settings);
    }

    // ===== 滑块回调 =====

    private void UpdateValueLabels()
    {
        LineSpacingValue.Text = $"{LineSpacingSlider.Value:F1}x";
        BodySizeValue.Text = $"{(int)BodySizeSlider.Value}pt";
        H1SizeValue.Text = $"{(int)H1SizeSlider.Value}pt";
        H2SizeValue.Text = $"{(int)H2SizeSlider.Value}pt";
        H3SizeValue.Text = $"{(int)H3SizeSlider.Value}pt";
        H4SizeValue.Text = $"{(int)H4SizeSlider.Value}pt";
        H5SizeValue.Text = $"{(int)H5SizeSlider.Value}pt";
        H6SizeValue.Text = $"{(int)H6SizeSlider.Value}pt";
    }

    private void OnLineSpacingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LineSpacingValue != null) LineSpacingValue.Text = $"{e.NewValue:F1}x";
    }

    private void OnBodySizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BodySizeValue != null) BodySizeValue.Text = $"{(int)e.NewValue}pt";
    }

    private void OnH1Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (H1SizeValue != null) H1SizeValue.Text = $"{(int)e.NewValue}pt";
    }

    private void OnH2Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (H2SizeValue != null) H2SizeValue.Text = $"{(int)e.NewValue}pt";
    }

    private void OnH3Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (H3SizeValue != null) H3SizeValue.Text = $"{(int)e.NewValue}pt";
    }

    private void OnH4Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (H4SizeValue != null) H4SizeValue.Text = $"{(int)e.NewValue}pt";
    }

    private void OnH5Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (H5SizeValue != null) H5SizeValue.Text = $"{(int)e.NewValue}pt";
    }

    private void OnH6Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (H6SizeValue != null) H6SizeValue.Text = $"{(int)e.NewValue}pt";
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

    private static IEnumerable<UIElement> FindSiblingHexBoxes(Button swatch)
    {
        if (swatch.Parent is Grid grid && grid.Parent is StackPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is Grid g)
                {
                    foreach (var gc in g.Children)
                        if (gc is UIElement uie) yield return uie;
                }
            }
        }
    }

    // ===== ComboBox 选项数据 =====

    private class ThemeOption
    {
        public string Display { get; set; }
        public string Value { get; set; }
        public bool IsCustom { get; }

        public ThemeOption(string name, string value, bool isCustom)
        {
            Value = value;
            IsCustom = isCustom;
            Display = isCustom ? $"{name} (自定义)" : name;
        }
    }
}
