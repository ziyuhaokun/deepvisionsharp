namespace DeepVisionSharp.Application.Models;

public enum ModelFamily
{
    Classification,
    Detection
}

public sealed record ModelCapabilities(
    ModelFamily Family,
    string TrainingBackendKey,
    string? InferenceBackendKey,
    string ArtifactHandlerKey,
    bool UsesInProcessTraining);

public static class ModelCapabilityCatalog
{
    private static readonly ModelCapabilities Classification = new(
        ModelFamily.Classification,
        TrainingBackendKey: BackendKeys.TorchSharpClassification,
        InferenceBackendKey: BackendKeys.InferenceTorchSharpClassification,
        ArtifactHandlerKey: ArtifactHandlerKeys.Classification,
        UsesInProcessTraining: true);

    private static readonly ModelCapabilities Detection = new(
        ModelFamily.Detection,
        TrainingBackendKey: BackendKeys.TorchSharpDetection,
        InferenceBackendKey: BackendKeys.InferenceTorchSharpDetection,
        ArtifactHandlerKey: ArtifactHandlerKeys.Detection,
        UsesInProcessTraining: true);

    public static bool IsSupported(string? modelType) =>
        TryResolve(modelType, out _);

    public static bool TryResolve(string? modelType, out ModelCapabilities capabilities)
    {
        if (!ModelTypeNames.TryNormalize(modelType, out var normalized))
        {
            capabilities = null!;
            return false;
        }

        if (ModelTypeNames.IsDetectionModelType(normalized))
        {
            capabilities = Detection;
            return true;
        }

        if (ModelTypeNames.IsClassificationModelType(normalized))
        {
            capabilities = Classification;
            return true;
        }

        capabilities = null!;
        return false;
    }

    public static bool TryResolve(string? modelType, string? executionEngine, out ModelCapabilities capabilities)
    {
        return TryResolve(modelType, out capabilities);
    }

    /// <summary>All capabilities for a family, used by project-scoped model pickers.</summary>
    public static IReadOnlyList<string> ModelTypesFor(ModelFamily family) =>
        family == ModelFamily.Detection
            ? ModelTypeNames.DetectionProductKeys
            : ModelTypeNames.ClassificationProductKeys;
}
