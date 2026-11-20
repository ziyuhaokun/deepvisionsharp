using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

using YuUI;

namespace DeepVisionSharp.Shell.Avalonia.Converters;

/// <summary>
/// 设置分区图标：把 <c>SettingsCategoryItem.Icon</c> 给的分区图标名换成 <see cref="IvIcons"/> 里的图形。
/// <para>
/// 表现层的设置分区只声明「用哪个图标」（Appearance → <c>Palette</c>、Storage → <c>Folder</c>、
/// About → <c>Info</c>、Advanced → <c>Settings</c>），具体画成什么由 Shell 决定，
/// 表现层不引用任何图形库。
/// </para>
/// <para>
/// 用法（页面上声明一次，再在绑定上引用）：
/// <c>&lt;conv:SettingsCategoryIconConverter x:Key="SettingsCategoryIcon" /&gt;</c>
/// <c>Data="{Binding Icon, Converter={StaticResource SettingsCategoryIcon}}"</c>
/// </para>
/// </summary>
public sealed class SettingsCategoryIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string name ? Resolve(name) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Geometry? Resolve(string name) => name switch
    {
        "Palette" => IvIcons.Palette,
        "Folder" => IvIcons.Folder,
        "Info" => IvIcons.Info,
        "Settings" => IvIcons.Settings,
        "Calculator" => IvIcons.Calculator,
        _ => null,
    };
}
