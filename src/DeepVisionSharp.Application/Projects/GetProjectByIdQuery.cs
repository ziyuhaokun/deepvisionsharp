using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Projects;

public sealed record GetProjectByIdQuery(Guid Id) : IQuery<Project?>;

public sealed class GetProjectByIdQueryHandler : IQueryHandler<GetProjectByIdQuery, Project?>
{
    private readonly IProjectRepository _projects;

    public GetProjectByIdQueryHandler(IProjectRepository projects)
    {
        _projects = projects;
    }

    public async Task<Result<Project?>> Handle(GetProjectByIdQuery request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        return Result<Project?>.Success(project);
    }
}
