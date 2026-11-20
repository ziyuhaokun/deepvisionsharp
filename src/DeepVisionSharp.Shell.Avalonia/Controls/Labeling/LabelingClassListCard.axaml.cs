using Avalonia;
using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

public partial class LabelingClassListCard : UserControl
{
    public static readonly StyledProperty<bool> ShowAddButtonProperty =
        AvaloniaProperty.Register<LabelingClassListCard, bool>(nameof(ShowAddButton), true);

    public LabelingClassListCard()
    {
        InitializeComponent();
    }

    public bool ShowAddButton
    {
        get => GetValue(ShowAddButtonProperty);
        set => SetValue(ShowAddButtonProperty, value);
    }
}