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

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
