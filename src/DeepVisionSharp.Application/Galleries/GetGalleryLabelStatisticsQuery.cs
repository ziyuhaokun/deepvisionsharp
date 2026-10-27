using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Galleries;

public sealed record GetGalleryLabelStatisticsQuery(Guid GalleryId)
    : IQuery<ProjectLabelStatisticsSnapshot>;

public sealed class GetGalleryLabelStatisticsQueryHandler
    : IQueryHandler<GetGalleryLabelStatisticsQuery, ProjectLabelStatisticsSnapshot>
{
    private readonly IProjectLabelStatisticsQueryService _labels;

    public GetGalleryLabelStatisticsQueryHandler(IProjectLabelStatisticsQueryService labels)
    {
        _labels = labels;
    }

    public async Task<Result<ProjectLabelStatisticsSnapshot>> Handle(
        GetGalleryLabelStatisticsQuery request,
        CancellationToken cancellationToken)
    {
        var snapshot = await _labels.GetGalleryStatisticsAsync(request.GalleryId, cancellationToken);
        return Result<ProjectLabelStatisticsSnapshot>.Success(snapshot);
    }
}
