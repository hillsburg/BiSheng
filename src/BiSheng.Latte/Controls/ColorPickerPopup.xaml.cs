using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace BiSheng.Latte.Controls;

/// <summary>
/// HSB 取色器：方块直接选饱和度(S)×明度(V)，色相条选 H。
/// 方块上点到的颜色与预览色块一致（所见即所得）。
/// </summary>
public partial class ColorPickerPopup : Window
{
    /// <summary>最终选中的颜色</summary>
    public Color SelectedColor { get; private set; }

    /// <summary>色相 0–360</summary>
    private double _hue;

    /// <summary>饱和度 0–1</summary>
    private double _saturation;

    /// <summary>明度 0–1</summary>
    private double _brightness;

    /// <summary>是否正在拖拽 SV 方块</summary>
    private bool _isDragging;

    /// <summary>程序化更新 UI 时抑制回写</summary>
    private bool _suppressEvents;

    public ColorPickerPopup(Color initialColor)
    {
        InitializeComponent();
        _suppressEvents = true;

        RgbToHsb(initialColor.R, initialColor.G, initialColor.B,
            out _hue, out _saturation, out _brightness);

        HueSlider.Value = _hue;
        RefreshSvHueBase();
        UpdateCursorFromSv();
        UpdateColorDisplay();

        _suppressEvents = false;
    }

    // ===== SV 方块 =====

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDragging = true;
        SvCanvas.CaptureMouse();
        UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void OnSvMouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        SvCanvas.ReleaseMouseCapture();
    }

    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            UpdateSvFromMouse(e.GetPosition(SvCanvas));
        }
    }

    /// <summary>从方块坐标取样：X→S，Y→V（上亮下暗）</summary>
    private void UpdateSvFromMouse(Point pos)
    {
        var x = Math.Clamp(pos.X, 0, SvCanvas.Width);
        var y = Math.Clamp(pos.Y, 0, SvCanvas.Height);

        _saturation = x / SvCanvas.Width;
        _brightness = 1.0 - (y / SvCanvas.Height);

        UpdateCursorFromSv();
        UpdateColorDisplay();
    }

    // ===== 色相条 =====

    private void OnHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents)
        {
            return;
        }

        _hue = e.NewValue;
        if (_hue >= 360)
        {
            _hue = 0;
        }

        RefreshSvHueBase();
        UpdateColorDisplay();
    }

    /// <summary>方块右侧纯色随色相变化，保证方块显示与取样一致</summary>
    private void RefreshSvHueBase()
    {
        var pure = HsbToRgb(_hue, 1, 1);
        SvHueStop.Color = pure;
    }

    // ===== HEX =====

    private void OnHexChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressEvents)
        {
            return;
        }

        var text = HexInput.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        try
        {
            var color = (Color)ColorConverter.ConvertFromString(text);
            _suppressEvents = true;

            RgbToHsb(color.R, color.G, color.B,
                out _hue, out _saturation, out _brightness);

            HueSlider.Value = _hue;
            RefreshSvHueBase();
            UpdateCursorFromSv();
            ApplyPreviewColor(color);

            _suppressEvents = false;
        }
        catch
        {
            // 无效 HEX，忽略
        }
    }

    // ===== 按钮 =====

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        SelectedColor = HsbToRgb(_hue, _saturation, _brightness);
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    // ===== UI =====

    private void UpdateCursorFromSv()
    {
        var x = _saturation * SvCanvas.Width;
        var y = (1.0 - _brightness) * SvCanvas.Height;
        Canvas.SetLeft(PickerCursor, x - 6);
        Canvas.SetTop(PickerCursor, y - 6);
    }

    /// <summary>预览与 HEX 使用当前 HSB，与方块取样一致</summary>
    private void UpdateColorDisplay()
    {
        var color = HsbToRgb(_hue, _saturation, _brightness);
        ApplyPreviewColor(color);

        _suppressEvents = true;
        HexInput.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        _suppressEvents = false;
    }

    private void ApplyPreviewColor(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        PreviewFill.Fill = brush;
        PreviewFill.InvalidateVisual();
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            PreviewFill.InvalidateVisual();
        }));
    }

    // ===== HSB ↔ RGB =====

    /// <summary>RGB (0-255) → HSB (H:0-360, S:0-1, V:0-1)</summary>
    private static void RgbToHsb(byte r, byte g, byte b,
        out double h, out double s, out double v)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        v = max;
        s = max == 0 ? 0 : delta / max;

        if (delta == 0)
        {
            h = 0;
        }
        else if (max == rd)
        {
            h = 60 * (((gd - bd) / delta) % 6);
        }
        else if (max == gd)
        {
            h = 60 * (((bd - rd) / delta) + 2);
        }
        else
        {
            h = 60 * (((rd - gd) / delta) + 4);
        }

        if (h < 0)
        {
            h += 360;
        }
    }

    /// <summary>HSB (H:0-360, S:0-1, V:0-1) → RGB (0-255)</summary>
    private static Color HsbToRgb(double h, double s, double v)
    {
        // 色相环：360 与 0 等价，避免落在最后一档边界
        if (h >= 360)
        {
            h = 0;
        }

        double c = v * s;
        double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
        double m = v - c;

        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
