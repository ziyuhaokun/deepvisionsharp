using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Domain.Repositories;

public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);

    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Project> CreateAsync(string name, string description, string type, CancellationToken cancellationToken = default);

    Task SaveAsync(Project project, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates only the descriptive fields. Statistics (sample count, progress) are owned by the
    /// gallery/annotation writers; a rename must not write back a stale snapshot of them.
    /// </summary>
    Task UpdateDetailsAsync(
        Guid projectId,
        string name,
        string description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 名称是否已被其它项目占用（大小写不敏感，与 UI 的 OrdinalIgnoreCase 口径对齐）。
    /// <paramref name="excludeProjectId"/> 用于编辑场景排除自身。
    /// </summary>
    Task<bool> ExistsByNameAsync(string name, Guid? excludeProjectId = null, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
