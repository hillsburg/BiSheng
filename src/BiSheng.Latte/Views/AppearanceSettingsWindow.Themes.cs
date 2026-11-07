using System.Windows;
using System.Windows.Controls;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Views;

/// <summary>
/// 外观设置 · 主题：下拉选择、复制为自定义主题、重命名与删除。
/// 内置主题只读；编辑始终作用于 <see cref="_editingTheme"/> 这份副本，保存时才写回用户主题文件。
/// </summary>
public partial class AppearanceSettingsWindow
{
    /// <summary>ComboBox 中的选项列表</summary>
    private readonly List<ThemeOption> _options = new();

    private void RefreshThemeOptions()
    {
        _options.Clear();
        _options.Add(new ThemeOption("跟随系统", "System", false));

        foreach (var t in ThemeManager.GetAllThemes())
        {
            _options.Add(new ThemeOption(t.Name, t.Name, !t.IsBuiltIn));
        }

        ThemeBox.ItemsSource = _options;
        ThemeBox.DisplayMemberPath = "Display";
    }

    /// <summary>取当前激活主题的可编辑副本；「跟随系统」时以 Latte 为编辑基准</summary>
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

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ThemeBox.SelectedIndex < 0)
        {
            return;
        }

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

    /// <summary>自定义主题才显示名称框与配色编辑区；内置主题只显示「复制并自定义」</summary>
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

    private void OnThemeNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || _editingTheme.IsBuiltIn)
        {
            return;
        }

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

    private void OnDeleteTheme(object sender, RoutedEventArgs e)
    {
        if (_editingTheme.IsBuiltIn)
        {
            return;
        }

        if (!AppDialog.ConfirmDanger($"确认删除主题【{_editingTheme.Name}】？", "确认删除"))
        {
            return;
        }

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

    /// <summary>主题下拉选项：Value 为主题名（或 "System"），Display 为显示文字</summary>
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
