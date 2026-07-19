using System.Globalization;

namespace NodeEditor.Converters;

/// <summary>
/// 反向布尔→可见性转换器：false → true(Visible), true → false(Collapsed)
/// MAUI 没有 Visibility 枚举，使用 IsVisible (bool) 控制
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

/// <summary>
/// 缩放百分比显示：1.0 → "100%"
/// </summary>
public class ZoomToPercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d) return $"{d:P0}";
        return "100%";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
