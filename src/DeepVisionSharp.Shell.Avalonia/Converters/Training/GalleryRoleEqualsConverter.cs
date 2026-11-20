using System.Globalization;
using Avalonia.Data.Converters;

namespace DeepVisionSharp.Shell.Avalonia.Converters.Training;

/// <summary>
/// 输入 GalleryDatasetRole 值与 ConverterParameter 中的角色名，输出布尔值，
/// 供 Avalonia 的 Classes 绑定把 WPF 的 DataTrigger 换成角色样式类。
/// </summary>
public sealed class GalleryRoleEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
