namespace DeepVisionSharp.Application.Models;

public enum ModelFamily
{
    Classification
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

    public static bool IsSupported(string? modelType) =>
        TryResolve(modelType, out _);

    public static bool TryResolve(string? modelType, out ModelCapabilities capabilities)
    {
        if (!ModelTypeNames.TryNormalize(modelType, out var normalized)
            || !ModelTypeNames.IsClassificationModelType(normalized))
        {
            capabilities = null!;
            return false;
        }

        capabilities = Classification;
        return true;
    }

    public static bool TryResolve(string? modelType, string? executionEngine, out ModelCapabilities capabilities)
    {
        return TryResolve(modelType, out capabilities);
    }
}
