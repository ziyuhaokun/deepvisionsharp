using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Domain.Repositories;

public interface IModelRepository
{
    Task<IReadOnlyList<TrainedModel>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TrainedModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid projectId, TrainedModel model, CancellationToken cancellationToken = default);

    Task<bool> ExistsWithNameAsync(
        Guid projectId,
        string name,
        Guid? excludeModelId = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default);
}
