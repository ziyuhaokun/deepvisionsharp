using System.Windows;
using System.Windows.Controls;

namespace DeepVisionSharp.Shell.Views.Labeling.Cards;

public partial class LabelingClassListCard : UserControl
{
    public static readonly DependencyProperty ShowAddButtonProperty = DependencyProperty.Register(
        nameof(ShowAddButton),
        typeof(bool),
        typeof(LabelingClassListCard),
        new PropertyMetadata(true));

    public LabelingClassListCard()
    {
        InitializeComponent();
    }

    /// <summary>Rail 是否装入「添加类别」按钮（OCR 字符集固定，设 false）。</summary>
    public bool ShowAddButton
    {
        get => (bool)GetValue(ShowAddButtonProperty);
        set => SetValue(ShowAddButtonProperty, value);
    }
}
