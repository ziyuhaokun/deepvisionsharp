using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using DeepVisionSharp.Presentation.ViewModels.Gallery;
using DeepVisionSharp.Shell.Avalonia.Rendering;

namespace DeepVisionSharp.Shell.Avalonia.Views.Dialogs.Contents;

public partial class ProjectLabelStatisticsDialogContent : UserControl
{
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private ProjectLabelStatisticsDialogState? _state;

    public ProjectLabelStatisticsDialogContent()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateState(DataContext as ProjectLabelStatisticsDialogState);
        DataContextChanged += (_, _) => UpdateState(IsLoaded ? DataContext as ProjectLabelStatisticsDialogState : null);
        Unloaded += (_, _) => UpdateState(null);
        SizeChanged += (_, _) => ScheduleRender();
        ActualThemeVariantChanged += (_, _) => ScheduleRender();
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            RenderChart();
        };
    }

    private void UpdateState(ProjectLabelStatisticsDialogState? state)
    {
        if (_state is not null)
        {
            _state.PropertyChanged -= OnStatePropertyChanged;
        }

        _state = state;
        if (state is not null)
        {
            state.PropertyChanged += OnStatePropertyChanged;
        }

        _renderTimer.Stop();
        RenderChart();
    }

    private void OnStatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectLabelStatisticsDialogState.Rows)
            or nameof(ProjectLabelStatisticsDialogState.GalleryRows)
            or nameof(ProjectLabelStatisticsDialogState.SelectedTopCount)
            or nameof(ProjectLabelStatisticsDialogState.SelectedChartType))
        {
            ScheduleRender();
        }
    }

    private void ScheduleRender()
    {
        if (_state is null)
        {
            return;
        }

        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void RenderChart()
    {
        if (_state is not { Rows.Count: > 0 } state)
        {
            ChartImage.Source = null;
            return;
        }

        var width = ChartHost.Bounds.Width > 0 ? ChartHost.Bounds.Width - ChartHost.Padding.Left - ChartHost.Padding.Right : Math.Max(Bounds.Width - 80, 720);
        ChartImage.Source = ProjectLabelStatisticsChartRenderer.Render(new ProjectLabelStatisticsChartRequest(
            state.SelectedChartType, state.Rows, state.GalleryRows, state.SelectedTopCount, state.TotalLabels, width,
            FindBrush("IvText1Brush"), FindBrush("IvText2Brush")));
    }

    private IBrush? FindBrush(string key) => this.TryFindResource(key, out var brush) ? brush as IBrush : null;
}
