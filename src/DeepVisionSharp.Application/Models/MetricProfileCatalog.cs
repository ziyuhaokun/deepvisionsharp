namespace DeepVisionSharp.Application.Models;

/// <summary>Resolves the metric display profile from the model type.</summary>
public static class MetricProfileCatalog
{
    public static MetricProfileKind ForModelType(string? modelType) =>
        ModelCapabilityCatalog.TryResolve(modelType, out _)
            ? MetricProfileKind.ClassificationAccuracy
            : MetricProfileKind.None;
}
