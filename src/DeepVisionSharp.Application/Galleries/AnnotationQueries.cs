using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Galleries;

public sealed record LoadAnnotationsQuery(Guid GalleryId, string FileName) : IQuery<AnnotationDocument>;

public sealed class LoadAnnotationsQueryHandler : IQueryHandler<LoadAnnotationsQuery, AnnotationDocument>
{
    private readonly IAnnotationWorkflowService _annotations;

    public LoadAnnotationsQueryHandler(IAnnotationWorkflowService annotations)
    {
        _annotations = annotations;
    }

    public async Task<Result<AnnotationDocument>> Handle(LoadAnnotationsQuery request, CancellationToken cancellationToken)
    {
        var document = await _annotations
            .LoadAsync(request.GalleryId, request.FileName, cancellationToken)
            .ConfigureAwait(false);
        return Result<AnnotationDocument>.Success(document);
    }
}

public sealed record GetSampleImagePathQuery(Guid ProjectId, Guid GalleryId, string FileName) : IQuery<string>;

public sealed class GetSampleImagePathQueryHandler : IQueryHandler<GetSampleImagePathQuery, string>
{
    private readonly IAnnotationWorkflowService _annotations;

    public GetSampleImagePathQueryHandler(IAnnotationWorkflowService annotations)
    {
        _annotations = annotations;
    }

    public Task<Result<string>> Handle(GetSampleImagePathQuery request, CancellationToken cancellationToken)
    {
        var path = _annotations.GetImagePath(request.ProjectId, request.GalleryId, request.FileName);
        return Task.FromResult(Result<string>.Success(path));
    }
}

public sealed record GetGallerySampleImageClassesQuery(Guid GalleryId, IReadOnlyList<string> FileNames)
    : IQuery<IReadOnlyDictionary<string, string>>;

public sealed class GetGallerySampleImageClassesQueryHandler
    : IQueryHandler<GetGallerySampleImageClassesQuery, IReadOnlyDictionary<string, string>>
{
    private readonly IAnnotationWorkflowService _annotations;

    public GetGallerySampleImageClassesQueryHandler(IAnnotationWorkflowService annotations)
    {
        _annotations = annotations;
    }

    public async Task<Result<IReadOnlyDictionary<string, string>>> Handle(
        GetGallerySampleImageClassesQuery request,
        CancellationToken cancellationToken)
    {
        var dictionary = await _annotations
            .LoadImageClassesBatchAsync(request.GalleryId, request.FileNames, cancellationToken)
            .ConfigureAwait(false);
        return Result<IReadOnlyDictionary<string, string>>.Success(dictionary);
    }
}
