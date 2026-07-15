using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace NodeEditor.Wpf.Converters;

/// <summary>
/// 反向布尔→可见性转换器：false → Visible, true → Collapsed
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Collapsed : Visibility.Visible;
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility v)
            return v != Visibility.Visible;
        return false;
    }
}