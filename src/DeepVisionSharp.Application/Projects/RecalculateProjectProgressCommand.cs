using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Projects;

public sealed record RecalculateProjectProgressCommand(Guid ProjectId) : ICommand<Unit>;

public sealed class RecalculateProjectProgressCommandHandler : ICommandHandler<RecalculateProjectProgressCommand, Unit>
{
    private readonly IProjectRepository _projects;
    private readonly IGalleryRepository _galleries;

    public RecalculateProjectProgressCommandHandler(IProjectRepository projects, IGalleryRepository galleries)
    {
        _projects = projects;
        _galleries = galleries;
    }

    public async Task<Result<Unit>> Handle(RecalculateProjectProgressCommand request, CancellationToken cancellationToken)
    {
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            return Result<Unit>.Failure("项目不存在");
        }

        var galleries = await _galleries.ListByProjectAsync(request.ProjectId, cancellationToken);
        var totalSamples = galleries.Sum(gallery => gallery.SampleCount);
        var totalAnnotated = galleries.Sum(gallery => gallery.AnnotatedCount);
        var progress = totalSamples > 0 ? (double)totalAnnotated / totalSamples : 0;

        var updated = project.WithDetails(
            project.Name,
            project.Description,
            project.Type,
            totalSamples,
            project.LabelCount,
            progress);

        await _projects.SaveAsync(updated, cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}
