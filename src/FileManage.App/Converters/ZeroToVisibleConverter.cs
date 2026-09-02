using System.Globalization;
using System.Windows.Data;
using System.Windows;

namespace FileManage.App.Converters;

/// <summary>
/// 数值为 0 时返回 Visible，否则返回 Collapsed（用于空状态提示）。
/// </summary>
public sealed class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
