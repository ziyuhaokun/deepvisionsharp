using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DeepVisionSharp.Shell.Rendering;
using DeepVisionSharp.Shell.ViewModels.Gallery;

namespace DeepVisionSharp.Shell.Views.Dialogs.Contents;

public partial class ProjectLabelStatisticsDialogContent : System.Windows.Controls.UserControl
{
    private DispatcherTimer? _renderDebounce;
    private ProjectLabelStatisticsDialogState? _state;

    public ProjectLabelStatisticsDialogContent()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_state is not null)
        {
            _state.PropertyChanged -= OnStatePropertyChanged;
        }

        _state = e.NewValue as ProjectLabelStatisticsDialogState;
        if (_state is not null)
        {
            _state.PropertyChanged += OnStatePropertyChanged;
        }

        RenderChart();
    }

    /// <summary>
    /// The state object outlives this control (it belongs to the gallery view model) and the control
    /// is created per dialog, so without unsubscribing here every open leaves a closed control
    /// referenced by the state — still re-rendering its chart whenever the user changes the filter.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _renderDebounce?.Stop();
        _renderDebounce = null;

        if (_state is not null)
        {
            _state.PropertyChanged -= OnStatePropertyChanged;
            _state = null;
        }
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectLabelStatisticsDialogState.Rows)
            or nameof(ProjectLabelStatisticsDialogState.GalleryRows)
            or nameof(ProjectLabelStatisticsDialogState.SelectedTopCount)
            or nameof(ProjectLabelStatisticsDialogState.SelectedChartType))
        {
            ScheduleRenderChart();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RenderChart();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleRenderChart();
    }

    private void ScheduleRenderChart()
    {
        _renderDebounce ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            (_, _) =>
            {
                _renderDebounce?.Stop();
                RenderChart();
            },
            Dispatcher);

        _renderDebounce.Stop();
        _renderDebounce.Start();
    }

    private void RenderChart()
    {
        if (ChartImage is null)
        {
            return;
        }

        if (_state is null || _state.Rows.Count == 0)
        {
            ChartImage.Source = null;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double chartWidth = ChartHost.ActualWidth > 0 ? ChartHost.ActualWidth : Math.Max(ActualWidth - 80, 720);

        ChartImage.Source = ProjectLabelStatisticsChartRenderer.Render(
            new ProjectLabelStatisticsChartRequest(
                _state.SelectedChartType,
                _state.Rows,
                _state.GalleryRows,
                _state.SelectedTopCount,
                _state.TotalLabels,
                chartWidth,
                dpi,
                TryFindBrush("TextFillColorPrimaryBrush"),
                TryFindBrush("TextFillColorSecondaryBrush")));
    }

    private System.Windows.Media.Brush? TryFindBrush(string key)
        => TryFindResource(key) as System.Windows.Media.Brush;
}
