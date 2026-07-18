using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BiSheng.Latte.Converters;

/// <summary>将资源字典键名解析为 Brush（用于 DynamicResource 键绑定）</summary>
public class ResourceKeyToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string key || string.IsNullOrWhiteSpace(key))
        {
            return Brushes.Gray;
        }

        if (Application.Current?.TryFindResource(key) is Brush brush)
        {
            return brush;
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
