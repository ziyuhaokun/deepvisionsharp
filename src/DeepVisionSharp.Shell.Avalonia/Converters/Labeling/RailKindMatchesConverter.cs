using System.Globalization;
using Avalonia.Data.Converters;

namespace DeepVisionSharp.Shell.Avalonia.Converters.Labeling;

/// <summary>
/// 输入 ActiveRailKind 的轨道名与 ConverterParameter 里的轨道名，输出是否匹配，
/// 供左侧 Detection / Classification 两条轨道与右栏框列表的 IsVisible 互斥切换。
/// </summary>
public sealed class RailKindMatchesConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => parameter is string expected
            && string.Equals(value?.ToString(), expected, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>多路布尔绑定取与，替代 WPF 的 MultiDataTrigger（样本区多个 IsVisible 条件合并）。</summary>
public sealed class AllTrueConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        => values.All(value => value is true);
}
