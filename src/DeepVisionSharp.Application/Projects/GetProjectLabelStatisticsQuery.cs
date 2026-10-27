using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Projects;

public sealed record GetProjectLabelStatisticsQuery(Guid ProjectId)
    : IQuery<ProjectLabelStatisticsSnapshot>;

public sealed class GetProjectLabelStatisticsQueryHandler
    : IQueryHandler<GetProjectLabelStatisticsQuery, ProjectLabelStatisticsSnapshot>
{
    private readonly IProjectLabelStatisticsQueryService _labels;

    public GetProjectLabelStatisticsQueryHandler(IProjectLabelStatisticsQueryService labels)
    {
        _labels = labels;
    }

    public async Task<Result<ProjectLabelStatisticsSnapshot>> Handle(
        GetProjectLabelStatisticsQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = await _labels.GetStatisticsAsync(request.ProjectId, cancellationToken);
        return Result<ProjectLabelStatisticsSnapshot>.Success(snapshot);
    }
}
