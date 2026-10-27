using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed record ModelArtifactContext(
    Guid ProjectId,
    string ModelName,
    string ModelType,
    string? BestModelPath,
    string? LastModelPath,
    string? OutputRoot,
    string? DataRoot,
    string? SourceCheckpointPath,
    IReadOnlyList<string> CandidateClasses,
    TrainingJobForm? Form,
    TrainingCheckpointSelection CheckpointSelection = TrainingCheckpointSelection.Best,
    Guid? TrainingJobId = null);
