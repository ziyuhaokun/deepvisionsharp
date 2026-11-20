using System.Windows;

namespace DeepVisionSharp.UI;

/// <summary>
/// TextBox / ComboBox 的占位文本。源 InCube.UI.Avalonia 用控件自带的 PlaceholderText 属性，
/// WPF 没有对应属性，改用附加属性承载，由 Themes/Styles 里的占位层读取。
/// </summary>
public static class IvPlaceholder
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(IvPlaceholder),
        new FrameworkPropertyMetadata(null));

    public static string? GetText(DependencyObject d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return (string?)d.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject d, string? value)
    {
        ArgumentNullException.ThrowIfNull(d);
        d.SetValue(TextProperty, value);
    }
}
