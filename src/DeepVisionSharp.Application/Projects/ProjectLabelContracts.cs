namespace DeepVisionSharp.Application.Projects;

public interface IProjectLabelService
{
    Task<IReadOnlyList<ProjectLabelOption>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectLabelOption> AddAsync(Guid projectId, string name, string color, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectLabelOption>> AddManyAsync(
        Guid projectId,
        IReadOnlyList<ProjectLabelSeed> labels,
        CancellationToken cancellationToken = default);

    Task RemoveAllAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectLabelOption> UpdateColorAsync(Guid projectId, Guid labelId, string color, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid projectId, string name, bool purgeAnnotations, CancellationToken cancellationToken = default);
}

public sealed record ProjectLabelOption(Guid Id, string Name, string Color);

public sealed record ProjectLabelSeed(string Name, string Color);
