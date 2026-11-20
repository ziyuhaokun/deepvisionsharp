using Avalonia;
using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

public partial class LabelingDrawToolsCard : UserControl
{
    public static readonly StyledProperty<bool> ShowRectangleProperty =
        AvaloniaProperty.Register<LabelingDrawToolsCard, bool>(nameof(ShowRectangle), true);

    public static readonly StyledProperty<bool> ShowPolygonProperty =
        AvaloniaProperty.Register<LabelingDrawToolsCard, bool>(nameof(ShowPolygon), true);

    public LabelingDrawToolsCard()
    {
        InitializeComponent();
    }

    public bool ShowRectangle
    {
        get => GetValue(ShowRectangleProperty);
        set => SetValue(ShowRectangleProperty, value);
    }

    public bool ShowPolygon
    {
        get => GetValue(ShowPolygonProperty);
        set => SetValue(ShowPolygonProperty, value);
    }
}