using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

using DeepVisionSharp.Shell.ViewModels.Inference;

namespace DeepVisionSharp.Shell.Views.Pages.Sections;

public partial class InferenceImageListSection : System.Windows.Controls.UserControl
{
    private InferenceViewModel? _viewModel;

    public InferenceImageListSection()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        LeftListPanel.SizeChanged += OnLeftListPanelSizeChanged;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Give the ListBox an explicit max height equal to the left panel body so it scrolls
    /// internally even if star-sizing still receives an unconstrained measure pass.
    /// </summary>
    public void UpdateListHeight()
    {
        if (LeftListPanel.ActualHeight <= 0)
        {
            return;
        }

        // Panel padding (4*2) + header (~52) + list margin; keep a floor so empty lists still look ok.
        const double headerAndChrome = 72;
        var listHeight = Math.Max(80, LeftListPanel.ActualHeight - headerAndChrome);
        ImageList.MaxHeight = listHeight;
        ImageList.Height = listHeight;
    }

    private void OnLeftListPanelSizeChanged(object sender, SizeChangedEventArgs e) => UpdateListHeight();

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = e.NewValue as InferenceViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InferenceViewModel.ImageCount)
            or nameof(InferenceViewModel.Images))
        {
            // After bulk import the visual tree may remeasure; re-clamp list height next layout pass.
            Dispatcher.BeginInvoke(UpdateListHeight, DispatcherPriority.Loaded);
        }
    }
}
