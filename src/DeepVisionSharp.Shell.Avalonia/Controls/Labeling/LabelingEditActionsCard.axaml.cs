using Avalonia;
using Avalonia.Controls;

namespace DeepVisionSharp.Shell.Avalonia.Controls.Labeling;

public partial class LabelingEditActionsCard : UserControl
{
    public static readonly StyledProperty<bool> ShowImportProperty =
        AvaloniaProperty.Register<LabelingEditActionsCard, bool>(nameof(ShowImport), true);

    public static readonly StyledProperty<bool> ShowMoveProperty =
        AvaloniaProperty.Register<LabelingEditActionsCard, bool>(nameof(ShowMove), true);

    public static readonly StyledProperty<bool> ShowClearProperty =
        AvaloniaProperty.Register<LabelingEditActionsCard, bool>(nameof(ShowClear));

    public static readonly StyledProperty<bool> ShowAutoFitProperty =
        AvaloniaProperty.Register<LabelingEditActionsCard, bool>(nameof(ShowAutoFit));

    public static readonly StyledProperty<bool> ShowTemplateProperty =
        AvaloniaProperty.Register<LabelingEditActionsCard, bool>(nameof(ShowTemplate));

    public static readonly StyledProperty<bool> ShowYoloInferProperty =
        AvaloniaProperty.Register<LabelingEditActionsCard, bool>(nameof(ShowYoloInfer));

    public LabelingEditActionsCard()
    {
        InitializeComponent();
    }

    public bool ShowImport
    {
        get => GetValue(ShowImportProperty);
        set => SetValue(ShowImportProperty, value);
    }

    public bool ShowMove
    {
        get => GetValue(ShowMoveProperty);
        set => SetValue(ShowMoveProperty, value);
    }

    public bool ShowClear
    {
        get => GetValue(ShowClearProperty);
        set => SetValue(ShowClearProperty, value);
    }

    public bool ShowAutoFit
    {
        get => GetValue(ShowAutoFitProperty);
        set => SetValue(ShowAutoFitProperty, value);
    }

    public bool ShowTemplate
    {
        get => GetValue(ShowTemplateProperty);
        set => SetValue(ShowTemplateProperty, value);
    }

    public bool ShowYoloInfer
    {
        get => GetValue(ShowYoloInferProperty);
        set => SetValue(ShowYoloInferProperty, value);
    }
}