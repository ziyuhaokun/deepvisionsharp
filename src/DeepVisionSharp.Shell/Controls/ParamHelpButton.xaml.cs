using System.Windows;
using System.Windows.Controls;

namespace DeepVisionSharp.Shell.Controls;

/// <summary>
/// Compact "?" help affordance. Click opens an opaque popover card with parameter docs.
/// </summary>
public partial class ParamHelpButton : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(ParamHelpButton),
        new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HelpTextProperty = DependencyProperty.Register(
        nameof(HelpText),
        typeof(string),
        typeof(ParamHelpButton),
        new PropertyMetadata(string.Empty));

    public ParamHelpButton()
    {
        InitializeComponent();
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string HelpText
    {
        get => (string)GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }
}
