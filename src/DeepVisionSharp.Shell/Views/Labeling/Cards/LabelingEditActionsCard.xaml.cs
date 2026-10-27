using System.Windows;
using System.Windows.Controls;

namespace DeepVisionSharp.Shell.Views.Labeling.Cards;

public partial class LabelingEditActionsCard : UserControl
{
    public static readonly DependencyProperty ShowImportProperty = DependencyProperty.Register(
        nameof(ShowImport),
        typeof(bool),
        typeof(LabelingEditActionsCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowMoveProperty = DependencyProperty.Register(
        nameof(ShowMove),
        typeof(bool),
        typeof(LabelingEditActionsCard),
        new PropertyMetadata(true));

    public static readonly DependencyProperty ShowAutoInferenceProperty = DependencyProperty.Register(
        nameof(ShowAutoInference),
        typeof(bool),
        typeof(LabelingEditActionsCard),
        new PropertyMetadata(false));

    public LabelingEditActionsCard()
    {
        InitializeComponent();
    }

    /// <summary>Rail 是否装入「导入图像」按钮。默认 true。</summary>
    public bool ShowImport
    {
        get => (bool)GetValue(ShowImportProperty);
        set => SetValue(ShowImportProperty, value);
    }

    /// <summary>Rail 是否装入「移动到其他图库」按钮。默认 true。</summary>
    public bool ShowMove
    {
        get => (bool)GetValue(ShowMoveProperty);
        set => SetValue(ShowMoveProperty, value);
    }

    /// <summary>Rail 是否装入自动推理按钮；再与 Inference.IsVisible 求交。</summary>
    public bool ShowAutoInference
    {
        get => (bool)GetValue(ShowAutoInferenceProperty);
        set => SetValue(ShowAutoInferenceProperty, value);
    }
}
