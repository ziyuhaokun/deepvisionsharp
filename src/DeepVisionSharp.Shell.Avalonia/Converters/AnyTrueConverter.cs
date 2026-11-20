using System.Globalization;
using Avalonia.Data.Converters;

namespace DeepVisionSharp.Shell.Avalonia.Converters;

/// <summary>多路布尔或：任一为 true 即显示（用于合并多个遮罩条件）。</summary>
public sealed class AnyTrueConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values.Any(value => value is true);
}
