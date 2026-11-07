using System.Windows;
using System.Windows.Controls;
using BiSheng.Latte.Models;
using BiSheng.Latte.Services;

namespace BiSheng.Latte.Views;

/// <summary>
/// 外观设置 · 字体与字号：编辑器字体按主题分别保存（切换主题时下拉跟着变），
/// 行间距 / 正文与各级标题字号由滑块调整，数值标签实时刷新。
/// </summary>
public partial class AppearanceSettingsWindow
{
    // ===== 编辑器字体 =====

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

    // ===== 滑块数值标签 =====

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

    // 以下回调在 InitializeComponent 期间就可能触发（XAML 设置 Minimum 时），此时标签尚未创建，需判空

    private void OnLineSpacingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LineSpacingValue != null)
        {
            LineSpacingValue.Text = $"{e.NewValue:F1}x";
        }
    }

    private void OnBodySizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetPointLabel(BodySizeValue, e.NewValue);
    }

    private void OnH1Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetPointLabel(H1SizeValue, e.NewValue);
    }

    private void OnH2Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetPointLabel(H2SizeValue, e.NewValue);
    }

    private void OnH3Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetPointLabel(H3SizeValue, e.NewValue);
    }

    private void OnH4Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetPointLabel(H4SizeValue, e.NewValue);
    }

    private void OnH5Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetPointLabel(H5SizeValue, e.NewValue);
    }

    private void OnH6Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        SetPointLabel(H6SizeValue, e.NewValue);
    }

    /// <summary>以整数磅值显示字号；标签未创建时跳过</summary>
    private static void SetPointLabel(TextBlock? label, double value)
    {
        if (label != null)
        {
            label.Text = $"{(int)value}pt";
        }
    }
}
