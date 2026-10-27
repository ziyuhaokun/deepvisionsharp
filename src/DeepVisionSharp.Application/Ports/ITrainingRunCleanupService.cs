using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// Safely deletes a single training job's OutputRoot under the project's runs/default tree.
/// </summary>
public interface ITrainingRunCleanupService
{
    Task<Result<TrainingRunCleanupResult>> DeleteRunAsync(
        Guid projectId,
        string outputRoot,
        CancellationToken cancellationToken = default);
}

public sealed record TrainingRunCleanupResult(
    bool DirectoryDeleted,
    bool AlreadyMissing,
    string? DeletedPath);
