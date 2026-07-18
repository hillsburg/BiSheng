using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BiSheng.Latte.Views;

/// <summary>
/// 主题化应用弹窗：替代系统 MessageBox，样式跟随外观设置。
/// </summary>
public partial class AppDialogWindow : Window
{
    /// <summary>用户选择结果</summary>
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private readonly MessageBoxButton _buttons;
    private Button? _defaultButton;

    /// <summary>创建弹窗实例</summary>
    public AppDialogWindow(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage icon)
    {
        InitializeComponent();

        _buttons = buttons;
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? ResolveDefaultTitle(icon) : title;
        MessageText.Text = message;
        ApplyVisualKind(icon);
        BuildButtons(buttons, icon);
    }

    /// <summary>根据图标类型设置强调色与徽章</summary>
    private void ApplyVisualKind(MessageBoxImage icon)
    {
        var (accent, badgeBg, glyph, glyphFg) = icon switch
        {
            MessageBoxImage.Error => (
                (Brush)FindResource("Brush.Danger"),
                (Brush)FindResource("Brush.SurfaceAlt"),
                "!",
                (Brush)FindResource("Brush.Danger")),
            MessageBoxImage.Warning => (
                (Brush)FindResource("Brush.AccentHover"),
                (Brush)FindResource("Brush.Selected"),
                "!",
                (Brush)FindResource("Brush.AccentHover")),
            MessageBoxImage.Question => (
                (Brush)FindResource("Brush.Accent"),
                (Brush)FindResource("Brush.Selected"),
                "?",
                (Brush)FindResource("Brush.Accent")),
            MessageBoxImage.Information => (
                (Brush)FindResource("Brush.Accent"),
                (Brush)FindResource("Brush.Selected"),
                "i",
                (Brush)FindResource("Brush.Accent")),
            _ => (
                (Brush)FindResource("Brush.Accent"),
                (Brush)FindResource("Brush.Selected"),
                "·",
                (Brush)FindResource("Brush.TextSecondary")),
        };

        AccentBar.Background = accent;
        IconBadge.Background = badgeBg;
        IconText.Text = glyph;
        IconText.Foreground = glyphFg;
    }

    /// <summary>构建底部按钮</summary>
    private void BuildButtons(MessageBoxButton buttons, MessageBoxImage icon)
    {
        ButtonPanel.Children.Clear();

        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("确定", MessageBoxResult.OK, isPrimary: true, isDanger: false);
                break;

            case MessageBoxButton.OKCancel:
                AddButton("取消", MessageBoxResult.Cancel, isPrimary: false, isDanger: false);
                AddButton("确定", MessageBoxResult.OK, isPrimary: true, isDanger: false);
                break;

            case MessageBoxButton.YesNo:
                AddButton("否", MessageBoxResult.No, isPrimary: false, isDanger: false);
                AddButton("是", MessageBoxResult.Yes, isPrimary: true, isDanger: icon == MessageBoxImage.Warning);
                break;

            case MessageBoxButton.YesNoCancel:
                AddButton("取消", MessageBoxResult.Cancel, isPrimary: false, isDanger: false);
                AddButton("否", MessageBoxResult.No, isPrimary: false, isDanger: false);
                AddButton("是", MessageBoxResult.Yes, isPrimary: true, isDanger: icon == MessageBoxImage.Warning);
                break;
        }

        Loaded += (_, _) => _defaultButton?.Focus();
    }

    /// <summary>添加单个按钮</summary>
    private void AddButton(string label, MessageBoxResult result, bool isPrimary, bool isDanger)
    {
        var styleKey = isDanger
            ? "DialogButtonDanger"
            : isPrimary
                ? "DialogButtonPrimary"
                : "DialogButtonSecondary";

        var button = new Button
        {
            Content = label,
            Style = (Style)FindResource(styleKey),
            IsDefault = isPrimary,
            IsCancel = result is MessageBoxResult.Cancel or MessageBoxResult.No && !isPrimary,
        };

        button.Click += (_, _) =>
        {
            Result = result;
            DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
            Close();
        };

        if (isPrimary)
        {
            _defaultButton = button;
        }

        ButtonPanel.Children.Add(button);
    }

    /// <summary>无标题时按图标推断默认标题</summary>
    private static string ResolveDefaultTitle(MessageBoxImage icon) => icon switch
    {
        MessageBoxImage.Error => "错误",
        MessageBoxImage.Warning => "警告",
        MessageBoxImage.Question => "确认",
        MessageBoxImage.Information => "提示",
        _ => "BiSheng",
    };

    /// <summary>支持 Esc 关闭</summary>
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Result = _buttons switch
            {
                MessageBoxButton.OK => MessageBoxResult.OK,
                MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
                MessageBoxButton.YesNo => MessageBoxResult.No,
                MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
                _ => MessageBoxResult.None,
            };

            DialogResult = false;
            Close();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
