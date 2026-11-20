using Avalonia.Controls;
using Avalonia.Media;
using DeepVisionSharp.Presentation.ViewModels.Inference;
using DeepVisionSharp.Shell.Avalonia.Rendering;

namespace DeepVisionSharp.Shell.Avalonia.Views.Dialogs.Contents;

public partial class InferenceBatchReportDialogContent : UserControl
{
    public InferenceBatchReportDialogContent()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderCharts();
        DataContextChanged += (_, _) => RenderCharts();
        ActualThemeVariantChanged += (_, _) => RenderCharts();
    }

    private void RenderCharts()
    {
        var images = IsLoaded && DataContext is InferenceBatchReport report
            ? InferenceBatchReportChartRenderer.RenderAll(report, new InferenceBatchReportChartTheme(
                FindBrush("IvText1Brush", Brushes.Black),
                FindBrush("IvText2Brush", Brushes.DimGray),
                FindBrush("IvStrokeBrush", Brushes.LightGray),
                FindBrush("IvSurface2Brush", Brushes.White),
                FindBrush("IvStrokeBrush", Brushes.LightGray)))
            : null;

        ConfusionChartImage.Source = images?.ConfusionMatrix;
        RateChartImage.Source = images?.RateBars;
        SuccessFailChartImage.Source = images?.SuccessFail;
        LatencyChartImage.Source = images?.LatencyBars;
    }

    private IBrush FindBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, out var brush) && brush is IBrush value ? value : fallback;
}
