using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Views.Labeling.Cards;

public partial class LabelingBoxListCard : UserControl
{
    public LabelingBoxListCard()
    {
        InitializeComponent();
    }

    private void OnRowClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: BoxShapeViewModel box }
            && DataContext is LabelingViewModel viewModel)
        {
            viewModel.Editor.SelectedBox = box;
        }
    }
}
