using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Projects;

/// <summary>
/// Dashboard overview: every project plus the resolved path of its thumbnail candidate.
/// Replaces the N+1 per-project gallery/sample/path queries with two batched store calls
/// inside a single handler.
/// </summary>
public sealed record GetDashboardProjectsQuery : IQuery<IReadOnlyList<DashboardProjectDto>>;

public sealed record DashboardProjectDto(
    Guid Id,
    string Name,
    string Description,
    string Type,
    int SampleCount,
    int LabelCount,
    double Progress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUpdatedAt,
    string? ThumbnailPath);

public sealed class GetDashboardProjectsQueryHandler
    : IQueryHandler<GetDashboardProjectsQuery, IReadOnlyList<DashboardProjectDto>>
{
    private readonly IProjectRepository _projects;
    private readonly IGalleryRepository _galleries;
    private readonly IAnnotationWorkflowService _annotations;

    public GetDashboardProjectsQueryHandler(
        IProjectRepository projects,
        IGalleryRepository galleries,
        IAnnotationWorkflowService annotations)
    {
        _projects = projects;
        _galleries = galleries;
        _annotations = annotations;
    }

    public async Task<Result<IReadOnlyList<DashboardProjectDto>>> Handle(
        GetDashboardProjectsQuery request,
        CancellationToken cancellationToken)
    {
        var projects = await _projects.ListAsync(cancellationToken).ConfigureAwait(false);
        var candidates = await _galleries.ListThumbnailCandidatesAsync(cancellationToken).ConfigureAwait(false);

        // 逐项写入而非 ToDictionary：即使出现异常重复候选也只取最后一个，不抛异常。
        var candidatesByProject = new Dictionary<Guid, ProjectThumbnailCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            candidatesByProject[candidate.ProjectId] = candidate;
        }

        var rows = projects
            .OrderByDescending(project => project.LastUpdatedAt)
            .Select(project =>
            {
                var thumbnailPath = candidatesByProject.TryGetValue(project.Id, out var candidate)
                    ? _annotations.GetImagePath(project.Id, candidate.GalleryId, candidate.FileName)
                    : null;

                return new DashboardProjectDto(
                    project.Id,
                    project.Name,
                    project.Description,
                    project.Type,
                    project.SampleCount,
                    project.LabelCount,
                    project.Progress,
                    project.CreatedAt,
                    project.LastUpdatedAt,
                    thumbnailPath);
            })
            .ToArray();

        return Result<IReadOnlyList<DashboardProjectDto>>.Success(rows);
    }
}