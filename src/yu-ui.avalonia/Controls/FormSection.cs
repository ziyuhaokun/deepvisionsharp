using Avalonia;
using Avalonia.Controls;

namespace YuUI.Controls;

/// <summary>
/// 分组容器：上缘 32 高标题带 + 1px 描边主体。
/// 对应 WPF 版 YuUI.Controls.FormSection，同时承担 WPF GroupBox 的职责
/// （Avalonia 没有 GroupBox）。
/// </summary>
public class FormSection : ContentControl
{
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<FormSection, object?>(nameof(Header));

    public static readonly StyledProperty<bool> HasHeaderProperty =
        AvaloniaProperty.Register<FormSection, bool>(nameof(HasHeader));

    protected override Type StyleKeyOverride => typeof(FormSection);

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool HasHeader
    {
        get => GetValue(HasHeaderProperty);
        private set => SetValue(HasHeaderProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HeaderProperty)
        {
            HasHeader = change.GetNewValue<object?>() is not null;
        }
    }
}
