using System.Windows;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;
using Microsoft.Win32;

namespace BiSheng.Latte.Views;

/// <summary>
/// 外观与样式设置弹窗：统一主题选择 + 自定义颜色编辑。
/// 按职责拆分为多个 partial 文件：
/// 本文件负责初始化与保存 / 应用 / 导入导出；
/// .Themes 负责主题切换与自定义主题管理；.Colors 负责颜色行与取色；
/// .Typography 负责字体与字号滑块；.Layout 负责布局页卡片与托盘选项。
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

    /// <summary>是否正在程序化更新 UI（防止事件循环）</summary>
    private bool _suppressEvents;

    public AppearanceSettingsWindow()
    {
        InitializeComponent();
        _settings = AppearanceSettings.Load();
        LoadCurrentSettings();
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

    /// <summary>把界面上的字体、字号、托盘选项写回内存中的设置（不落盘）</summary>
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

    // ===== 保存 / 取消 / 应用 =====

    private void OnSave(object sender, RoutedEventArgs e)
    {
        CollectCurrentSettings();

        // 保存自定义主题
        if (!_editingTheme.IsBuiltIn)
        {
            ThemeManager.SaveUserTheme(_editingTheme);
        }

        _settings.Save();
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnApply(object sender, RoutedEventArgs e)
    {
        CollectCurrentSettings();

        if (!_editingTheme.IsBuiltIn)
        {
            ThemeManager.SaveUserTheme(_editingTheme);
        }

        _settings.Save();

        // 通知宿主窗口重新应用外观
        ApplyCallback?.Invoke(_settings);
    }

    // ===== 导入 / 导出 =====

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

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            if (!_editingTheme.IsBuiltIn)
            {
                ThemeManager.SaveUserTheme(_editingTheme);
            }

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

        if (dialog.ShowDialog() != true)
        {
            return;
        }

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
}
