using Avalonia;
using Avalonia.Controls;

namespace DeepVisionSharp.UI.Avalonia.Controls;

/// <summary>
/// 表单行：左侧标题 + 可选说明，右侧内容；上缘 1px 分隔线，最小高度 40。
/// 对应 WPF 版 DeepVisionSharp.UI.Avalonia.Controls.FormRow。
/// </summary>
public class FormRow : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<FormRow, object?>(nameof(Header));

    public static readonly StyledProperty<string?> DescriptionProperty =
        AvaloniaProperty.Register<FormRow, string?>(nameof(Description));

    protected override Type StyleKeyOverride => typeof(FormRow);

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
