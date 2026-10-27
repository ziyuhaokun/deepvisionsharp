using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

using DeepVisionSharp.Shell.ViewModels.Training;

namespace DeepVisionSharp.Shell.Views.Training.Sections;

public partial class TrainingResourcesSection : System.Windows.Controls.UserControl
{
    private TrainingViewModel? _viewModel;

    public TrainingResourcesSection()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Log.PropertyChanged -= OnLogPropertyChanged;
        }

        _viewModel = e.NewValue as TrainingViewModel;
        if (_viewModel is not null)
        {
            _viewModel.Log.PropertyChanged += OnLogPropertyChanged;
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.Log.PropertyChanged -= OnLogPropertyChanged;
            _viewModel = null;
        }
    }

    private void OnLogPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrainingLogViewModel.LiveLog)
            && _viewModel is { Log.AutoScrollLog: true })
        {
            LogScrollViewer.Dispatcher.InvokeAsync(
                () => LogScrollViewer.ScrollToVerticalOffset(LogScrollViewer.ExtentHeight),
                DispatcherPriority.Loaded);
        }
    }
}
