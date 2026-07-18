using System.Globalization;
using System.Windows.Data;
using BiSheng.Latte.ViewModels;

namespace BiSheng.Latte.Converters;

/// <summary>判断 TreeView 节点是否为笔记叶节点</summary>
public class IsNoteItemConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is NoteItemViewModel;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
