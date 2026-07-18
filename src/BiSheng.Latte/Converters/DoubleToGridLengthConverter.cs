using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BiSheng.Latte.Converters;

/// <summary>double 列宽与 GridLength 互转，供面板列宽绑定</summary>
public class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double width && width > 0)
        {
            return new GridLength(width);
        }

        return new GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is GridLength gridLength && gridLength.IsAbsolute)
        {
            return gridLength.Value;
        }

        return 0d;
    }
}
