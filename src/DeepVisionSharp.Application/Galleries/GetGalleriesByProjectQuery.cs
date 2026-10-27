using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public sealed record GetGalleriesByProjectQuery(Guid ProjectId) : IQuery<IReadOnlyList<ProjectGallery>>;

public sealed class GetGalleriesByProjectQueryHandler
    : IQueryHandler<GetGalleriesByProjectQuery, IReadOnlyList<ProjectGallery>>
{
    private readonly IGalleryRepository _galleries;

    public GetGalleriesByProjectQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<IReadOnlyList<ProjectGallery>>> Handle(
        GetGalleriesByProjectQuery request,
        CancellationToken cancellationToken)
    {
        var galleries = await _galleries.ListByProjectAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<ProjectGallery>>.Success(galleries);
    }
}
