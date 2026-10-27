using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Projects;

public sealed record UpdateProjectCommand(Guid Id, string Name, string Description, string Type) : ICommand<Project>;

public sealed class UpdateProjectCommandHandler : ICommandHandler<UpdateProjectCommand, Project>
{
    private readonly IProjectRepository _projects;

    public UpdateProjectCommandHandler(IProjectRepository projects)
    {
        _projects = projects;
    }

    public async Task<Result<Project>> Handle(UpdateProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            return Result<Project>.Failure("项目不存在");
        }

        if (!ProjectCapabilityCatalog.IsActive(project.Type))
        {
            return Result<Project>.Failure(new ProjectTypeUnsupportedException(project.Type).Message);
        }

        var requestedType = request.Type.Trim();
        if (!string.Equals(project.Type, requestedType, StringComparison.OrdinalIgnoreCase))
        {
            return Result<Project>.Failure("项目类型创建后不可修改");
        }

        var name = request.Name.Trim();
        if (await _projects.ExistsByNameAsync(name, project.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result<Project>.Failure($"项目名称「{name}」已存在。");
        }

        // Only the descriptive fields are written. SaveAsync stores the whole row, so a rename that
        // ran while the labeling page was updating counters would write back this command's stale
        // SampleCount/Progress snapshot.
        var description = request.Description.Trim();
        await _projects.UpdateDetailsAsync(project.Id, name, description, cancellationToken).ConfigureAwait(false);

        var updated = project.WithDetails(
            name,
            description,
            project.Type,
            project.SampleCount,
            project.LabelCount,
            project.Progress);

        return Result<Project>.Success(updated);
    }
}
