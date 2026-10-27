using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Projects;

public sealed record DeleteProjectCommand(Guid Id) : ICommand;

public sealed class DeleteProjectCommandHandler : ICommandHandler<DeleteProjectCommand>
{
    private readonly IProjectRepository _projects;

    public DeleteProjectCommandHandler(IProjectRepository projects)
    {
        _projects = projects;
    }

    public async Task<Result> Handle(DeleteProjectCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (project is not null && !ProjectCapabilityCatalog.IsActive(project.Type))
        {
            return Result.Failure(new ProjectTypeUnsupportedException(project.Type).Message);
        }
        try
        {
            var deleted = await _projects.DeleteAsync(request.Id, cancellationToken).ConfigureAwait(false);
            return deleted ? Result.Success() : Result.Failure("未找到要删除的项目");
        }
        catch (ProjectBusyException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
