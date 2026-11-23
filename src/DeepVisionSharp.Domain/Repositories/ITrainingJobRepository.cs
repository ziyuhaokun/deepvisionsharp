using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Domain.Repositories;

public interface ITrainingJobRepository
{
    Task<TrainingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingJob>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingJob>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingJob>> GetRunningAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrainingJob>> GetByStatusesAsync(
        IReadOnlyList<TrainingJobStatus> statuses,
        CancellationToken cancellationToken = default);

    Task SaveAsync(TrainingJob job, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves <paramref name="job"/> only if the stored status is one of <paramref name="allowedCurrent"/>.
    /// The check and the write are one atomic statement, so a stale snapshot cannot overwrite a newer state.
    /// </summary>
    /// <returns>False when the stored status was not allowed (or the job no longer exists).</returns>
    Task<bool> SaveIfStatusAsync(
        TrainingJob job,
        IReadOnlyCollection<TrainingJobStatus> allowedCurrent,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
