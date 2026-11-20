using System.Windows;
using System.Windows.Controls;

namespace YuUI.Controls;

/// <summary>
/// 分组容器：上缘 32 高标题带 + 1px 描边主体。
/// <see cref="HasHeader"/> 由 <see cref="Header"/> 自动推导（源里是 private set + OnPropertyChanged），
/// 模板据此隐藏/显示标题带。
/// </summary>
public class FormSection : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(
            nameof(Header),
            typeof(object),
            typeof(FormSection),
            new PropertyMetadata(null, OnHeaderChanged));

    public static readonly DependencyProperty HasHeaderProperty =
        DependencyProperty.Register(nameof(HasHeader), typeof(bool), typeof(FormSection), new PropertyMetadata(false));

    static FormSection()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(FormSection), new FrameworkPropertyMetadata(typeof(FormSection)));
    }

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public bool HasHeader
    {
        get => (bool)GetValue(HasHeaderProperty);
        private set => SetValue(HasHeaderProperty, value);
    }

    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((FormSection)d).HasHeader = e.NewValue is not null;
    }
}
