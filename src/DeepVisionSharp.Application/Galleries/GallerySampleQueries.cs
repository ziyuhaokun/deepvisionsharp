using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public sealed record GetGallerySummaryByIdQuery(Guid GalleryId) : IQuery<ProjectGallery?>;

public sealed class GetGallerySummaryByIdQueryHandler : IQueryHandler<GetGallerySummaryByIdQuery, ProjectGallery?>
{
    private readonly IGalleryRepository _galleries;

    public GetGallerySummaryByIdQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<ProjectGallery?>> Handle(
        GetGallerySummaryByIdQuery request,
        CancellationToken cancellationToken)
    {
        var gallery = await _galleries.GetSummaryByIdAsync(request.GalleryId, cancellationToken).ConfigureAwait(false);
        return Result<ProjectGallery?>.Success(gallery);
    }
}

public sealed record ListGallerySamplesQuery(Guid GalleryId, int Skip, int Take) : IQuery<GallerySamplePage>;

public sealed class ListGallerySamplesQueryHandler : IQueryHandler<ListGallerySamplesQuery, GallerySamplePage>
{
    private readonly IGalleryRepository _galleries;

    public ListGallerySamplesQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<GallerySamplePage>> Handle(
        ListGallerySamplesQuery request,
        CancellationToken cancellationToken)
    {
        var page = await _galleries
            .ListSamplesAsync(request.GalleryId, request.Skip, request.Take, cancellationToken)
            .ConfigureAwait(false);
        return Result<GallerySamplePage>.Success(page);
    }
}

public sealed record GetGallerySampleQuery(Guid GalleryId, Guid SampleId) : IQuery<GallerySample?>;

public sealed class GetGallerySampleQueryHandler : IQueryHandler<GetGallerySampleQuery, GallerySample?>
{
    private readonly IGalleryRepository _galleries;

    public GetGallerySampleQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<GallerySample?>> Handle(
        GetGallerySampleQuery request,
        CancellationToken cancellationToken)
    {
        var sample = await _galleries
            .GetSampleAsync(request.GalleryId, request.SampleId, cancellationToken)
            .ConfigureAwait(false);
        return Result<GallerySample?>.Success(sample);
    }
}

public sealed record GetGallerySampleByFileNameQuery(Guid GalleryId, string FileName) : IQuery<GallerySample?>;

public sealed class GetGallerySampleByFileNameQueryHandler
    : IQueryHandler<GetGallerySampleByFileNameQuery, GallerySample?>
{
    private readonly IGalleryRepository _galleries;

    public GetGallerySampleByFileNameQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<GallerySample?>> Handle(
        GetGallerySampleByFileNameQuery request,
        CancellationToken cancellationToken)
    {
        var sample = await _galleries.GetSampleByFileNameAsync(
            request.GalleryId,
            request.FileName,
            cancellationToken).ConfigureAwait(false);
        return Result<GallerySample?>.Success(sample);
    }
}

public sealed record GetAdjacentGallerySampleQuery(Guid GalleryId, Guid CurrentSampleId, int Offset)
    : IQuery<GallerySample?>;

public sealed class GetAdjacentGallerySampleQueryHandler
    : IQueryHandler<GetAdjacentGallerySampleQuery, GallerySample?>
{
    private readonly IGalleryRepository _galleries;

    public GetAdjacentGallerySampleQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<GallerySample?>> Handle(
        GetAdjacentGallerySampleQuery request,
        CancellationToken cancellationToken)
    {
        var sample = await _galleries.GetAdjacentSampleAsync(
            request.GalleryId,
            request.CurrentSampleId,
            request.Offset,
            cancellationToken).ConfigureAwait(false);
        return Result<GallerySample?>.Success(sample);
    }
}

public sealed record GetGallerySampleIndexQuery(Guid GalleryId, Guid SampleId) : IQuery<int>;

public sealed class GetGallerySampleIndexQueryHandler : IQueryHandler<GetGallerySampleIndexQuery, int>
{
    private readonly IGalleryRepository _galleries;

    public GetGallerySampleIndexQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<int>> Handle(GetGallerySampleIndexQuery request, CancellationToken cancellationToken)
    {
        var index = await _galleries
            .GetSampleIndexAsync(request.GalleryId, request.SampleId, cancellationToken)
            .ConfigureAwait(false);
        return Result<int>.Success(index);
    }
}
