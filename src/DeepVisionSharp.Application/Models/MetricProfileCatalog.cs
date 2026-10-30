namespace DeepVisionSharp.Application.Models;

/// <summary>Resolves the metric display profile from the model type.</summary>
public static class MetricProfileCatalog
{
    public static MetricProfileKind ForModelType(string? modelType)
    {
        if (!ModelCapabilityCatalog.TryResolve(modelType, out var capabilities))
        {
            return MetricProfileKind.None;
        }

        return capabilities.Family switch
        {
            ModelFamily.Detection => MetricProfileKind.DetectionMap,
            ModelFamily.Classification => MetricProfileKind.ClassificationAccuracy,
            _ => MetricProfileKind.None
        };
    }
}
