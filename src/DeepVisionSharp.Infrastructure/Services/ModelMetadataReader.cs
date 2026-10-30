using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Models;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ModelMetadataReader : IModelMetadataReader
{
    private readonly ModelMetadataService _metadataService;

    public ModelMetadataReader(ModelMetadataService metadataService)
    {
        _metadataService = metadataService;
    }

    public ModelDisplayMetrics? Read(string checkpointPath)
    {
        var metadata = _metadataService.ReadMetadata(checkpointPath);
        if (metadata is null)
        {
            return null;
        }

        // New initializer writes ArtifactKind=base_weights + schema 2.
        // Legacy "create model from offline weights" wrote IsInitial=true without ArtifactKind.
        var isExplicitBaseWeights = string.Equals(metadata.ArtifactKind, "base_weights", StringComparison.Ordinal);
        var isLegacyInitialBaseWeights = metadata.IsInitial;
        var isBaseWeights = isExplicitBaseWeights || isLegacyInitialBaseWeights;

        var isSupportedModel = ModelTypeNames.IsKnownModelType(metadata.ModelType);
        var isCompleteTrainedModel = string.Equals(metadata.ArtifactKind, "trained_model", StringComparison.Ordinal)
            && !metadata.IsInitial
            && isSupportedModel
            && !string.IsNullOrWhiteSpace(metadata.DatasetFingerprint);

        // Schema 2 is current; legacy initial .info may omit schema_version (0 after deserialize).
        var schemaOk = metadata.SchemaVersion == 2
            || (isLegacyInitialBaseWeights && metadata.SchemaVersion is 0 or 1);

        var hasSupportedMetadata = schemaOk && (isBaseWeights || isCompleteTrainedModel);

        int? inputSize = hasSupportedMetadata && metadata.InputSize > 0
            ? metadata.InputSize
            : null;
        var inputChannels = metadata.InputChannels is 1 or 3 ? metadata.InputChannels : 3;

        return new ModelDisplayMetrics(
            metadata.Epochs,
            metadata.BestMap,
            metadata.BestMap50,
            HasMetadata: hasSupportedMetadata,
            InputSize: inputSize,
            InputChannels: inputChannels);
    }
}
