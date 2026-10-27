using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace DeepVisionSharp.Shell.Converters;

/// <summary>
/// 生成与元素当前尺寸匹配的圆角裁剪几何（用于封面按圆角裁切，避免 ClipToBounds 矩形方切）。
/// 绑定元素的 ActualWidth / ActualHeight；<paramref name="parameter"/> 为可选圆角半径（默认 8）。
/// </summary>
public sealed class RoundedClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2
            && values[0] is double width && width > 0
            && values[1] is double height && height > 0)
        {
            var radius = parameter is string text
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 8;
            radius = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));

            var geometry = new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
            geometry.Freeze();
            return geometry;
        }

        return DependencyProperty.UnsetValue;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
