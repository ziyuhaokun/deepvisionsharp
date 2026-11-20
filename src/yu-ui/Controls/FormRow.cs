using System.Windows;
using System.Windows.Controls;

namespace YuUI.Controls;

/// <summary>
/// 表单行：左侧标题 + 可选说明，右侧内容；上缘 1px 分隔线，最小高度 40。
/// 形状与源 InCube.UI.Avalonia/Controls/FormRow.cs 一致：ContentControl + 两个属性。
/// </summary>
public class FormRow : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(object), typeof(FormRow), new PropertyMetadata(defaultValue: null));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(FormRow), new PropertyMetadata(defaultValue: null));

    static FormRow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FormRow), new FrameworkPropertyMetadata(typeof(FormRow)));
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
