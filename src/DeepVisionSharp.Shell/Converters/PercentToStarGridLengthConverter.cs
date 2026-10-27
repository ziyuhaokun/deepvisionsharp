using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DeepVisionSharp.Shell.Converters;

/// <summary>将 0–100 百分比映射为 Grid star 宽度，供训练/验证对照条使用。</summary>
public sealed class PercentToStarGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var percent = value is double number && double.IsFinite(number) ? number : 0;
        if (percent < 0)
        {
            percent = 0;
        }

        return new GridLength(percent, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
