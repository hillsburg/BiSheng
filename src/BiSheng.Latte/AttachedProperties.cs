using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BiSheng.Latte;

/// <summary>
/// WPF 附加属性集合
/// TextBoxAutoSelect：当 TextBox 变为可见时自动聚焦并全选（用于内联重命名）
/// </summary>
public static class TextBoxAutoSelect
{
    /// <summary>附加属性：是否启用自动聚焦和全选</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(TextBoxAutoSelect),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            if ((bool)e.NewValue)
                textBox.IsVisibleChanged += OnTextBoxVisibleChanged;
            else
                textBox.IsVisibleChanged -= OnTextBoxVisibleChanged;
        }
    }

    private static void OnTextBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox textBox && textBox.IsVisible)
        {
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                textBox.Focus();
                textBox.SelectAll();
            }, DispatcherPriority.Background);
        }
    }
}
