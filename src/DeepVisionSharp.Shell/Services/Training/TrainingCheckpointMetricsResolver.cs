using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Services.Training.Metrics;
using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.Services.Training;

internal static class TrainingCheckpointMetricsResolver
{
    public static (string ValueText, string EpochText) ResolveBest(
        IReadOnlyList<TrainingMetricPoint> history,
        string? modelType)
    {
        var presenter = TrainingMetricsPresenterCatalog.ForModelType(modelType);
        if (presenter.Profile == MetricProfileKind.None)
        {
            return ("—", string.Empty);
        }

        var best = history
            .Where(point => presenter.PrimaryQualitySelector(point).HasValue)
            .MaxBy(point => presenter.PrimaryQualitySelector(point));

        if (best is null || presenter.PrimaryQualitySelector(best) is not double quality)
        {
            return ("—", string.Empty);
        }

        return ($"{quality * 100:0.##}%", $"第 {best.Epoch} 轮");
    }

    public static (string ValueText, string EpochText) ResolveLast(
        IReadOnlyList<TrainingMetricPoint> history,
        string? modelType)
    {
        var presenter = TrainingMetricsPresenterCatalog.ForModelType(modelType);
        if (presenter.Profile == MetricProfileKind.None || history.Count == 0)
        {
            return ("—", string.Empty);
        }

        var last = history
            .Where(point => presenter.PrimaryQualitySelector(point).HasValue)
            .MaxBy(point => point.Epoch);

        if (last is null || presenter.PrimaryQualitySelector(last) is not double quality)
        {
            return ("—", string.Empty);
        }

        return ($"{quality * 100:0.##}%", $"第 {last.Epoch} 轮");
    }
}
