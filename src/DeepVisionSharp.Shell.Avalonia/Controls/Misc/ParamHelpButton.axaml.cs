using Avalonia;
using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Controls;

public partial class ParamHelpButton : UserControl
{
    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<ParamHelpButton, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> HelpTextProperty =
        AvaloniaProperty.Register<ParamHelpButton, string>(nameof(HelpText), string.Empty);

    public ParamHelpButton() => InitializeComponent();

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string HelpText
    {
        get => GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }
}
