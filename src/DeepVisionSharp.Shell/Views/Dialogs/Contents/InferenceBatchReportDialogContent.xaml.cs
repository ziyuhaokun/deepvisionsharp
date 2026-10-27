using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DeepVisionSharp.Shell.Rendering;
using DeepVisionSharp.Shell.ViewModels.Inference;

namespace DeepVisionSharp.Shell.Views.Dialogs.Contents;

public partial class InferenceBatchReportDialogContent : System.Windows.Controls.UserControl
{
    private DispatcherTimer? _renderDebounce;
    private InferenceBatchReport? _report;

    public InferenceBatchReportDialogContent()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _report = e.NewValue as InferenceBatchReport;
        RenderCharts();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RenderCharts();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleRenderCharts();
    }

    private void ScheduleRenderCharts()
    {
        _renderDebounce ??= new DispatcherTimer(
            TimeSpan.FromMilliseconds(120),
            DispatcherPriority.Background,
            (_, _) =>
            {
                _renderDebounce?.Stop();
                RenderCharts();
            },
            Dispatcher);

        _renderDebounce.Stop();
        _renderDebounce.Start();
    }

    private void RenderCharts()
    {
        if (ConfusionChartImage is null
            || RateChartImage is null
            || SuccessFailChartImage is null
            || LatencyChartImage is null)
        {
            return;
        }

        if (_report is null)
        {
            ConfusionChartImage.Source = null;
            RateChartImage.Source = null;
            SuccessFailChartImage.Source = null;
            LatencyChartImage.Source = null;
            return;
        }

        var theme = BuildTheme();
        var images = InferenceBatchReportChartRenderer.RenderAll(_report, theme);
        ConfusionChartImage.Source = images.ConfusionMatrix;
        RateChartImage.Source = images.RateBars;
        SuccessFailChartImage.Source = images.SuccessFail;
        LatencyChartImage.Source = images.LatencyBars;
    }

    private InferenceBatchReportChartTheme BuildTheme()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new InferenceBatchReportChartTheme(
            dpi,
            SnapshotBrush("TextFillColorPrimaryBrush", 240, 240, 240, 255),
            SnapshotBrush("TextFillColorSecondaryBrush", 180, 180, 180, 255),
            SnapshotBrush("ControlStrokeColorDefaultBrush", 255, 255, 255, 60),
            SnapshotBrush("ControlFillColorSecondaryBrush", 255, 255, 255, 18),
            SnapshotBrush("ControlStrokeColorDefaultBrush", 255, 255, 255, 48));
    }

    /// <summary>Copy resource color into a frozen SolidColorBrush (never freeze shared DynamicResource brushes).</summary>
    private Brush SnapshotBrush(string key, byte r, byte g, byte b, byte a)
    {
        if (TryFindResource(key) is SolidColorBrush solid)
        {
            var copy = new SolidColorBrush(solid.Color);
            copy.Freeze();
            return copy;
        }

        var fallback = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        fallback.Freeze();
        return fallback;
    }
}
