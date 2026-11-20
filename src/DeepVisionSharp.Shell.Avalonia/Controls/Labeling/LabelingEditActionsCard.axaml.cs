using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

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

    public bool ShowYoloInfer
    {
        get => GetValue(ShowYoloInferProperty);
        set => SetValue(ShowYoloInferProperty, value);
    }

    /// <summary>画布复位属于页面能力（AnnotationCanvas.FitToView），卡片只把点击往上抛。</summary>
    public event EventHandler<RoutedEventArgs>? AutoFitRequested;

    private void OnAutoFitClick(object? sender, RoutedEventArgs e) => AutoFitRequested?.Invoke(this, e);
}