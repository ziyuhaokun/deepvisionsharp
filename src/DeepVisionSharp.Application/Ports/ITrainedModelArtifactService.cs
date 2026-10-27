using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Ports;

public interface ITrainedModelArtifactService
{
    Result<string> ResolveBestCheckpointPath(ModelArtifactCheckpointRequest request);

    Result<string> ResolveCheckpointPath(ModelArtifactCheckpointRequest request);

    Task<Result<IReadOnlyList<string>>> RecoverClassesAsync(
        ModelArtifactClassRecoveryRequest request,
        CancellationToken cancellationToken);

    Task<Result<SavedTrainedModelArtifact>> SaveModelArtifactAsync(
        ModelArtifactSaveRequest request,
        CancellationToken cancellationToken);

    Result<string> TryRenameCheckpointFiles(string checkpointPath, string newModelName);
}

public sealed record ModelArtifactCheckpointRequest(
    string ModelType,
    string? BestModelPath,
    string? LastModelPath,
    string? OutputRoot,
    TrainingCheckpointSelection Selection = TrainingCheckpointSelection.Best);

public sealed record ModelArtifactClassRecoveryRequest(
    string ModelType,
    string? DataRoot,
    string? OutputRoot,
    string? CheckpointPath,
    IReadOnlyList<string>? CandidateClasses);

public sealed record ModelArtifactSaveRequest(
    Guid ProjectId,
    string ModelName,
    string ModelType,
    string SourceCheckpointPath,
    IReadOnlyList<string> Classes,
    TrainingJobForm Form,
    string? DataRoot = null,
    string? OutputRoot = null,
    string? BestModelPath = null,
    string? LastModelPath = null,
    TrainingCheckpointSelection CheckpointSelection = TrainingCheckpointSelection.Best,
    Guid? TrainingJobId = null);

public sealed record SavedTrainedModelArtifact(string CheckpointPath, string? MetadataPath);
