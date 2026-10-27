using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public sealed record GetGallerySampleAnchorQuery(Guid GalleryId) : IQuery<string?>;

public sealed class GetGallerySampleAnchorQueryHandler : IQueryHandler<GetGallerySampleAnchorQuery, string?>
{
    private readonly IGalleryRepository _galleries;

    public GetGallerySampleAnchorQueryHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<string?>> Handle(
        GetGallerySampleAnchorQuery request,
        CancellationToken cancellationToken)
    {
        var fileName = await _galleries.GetAnchorFileNameAsync(request.GalleryId, cancellationToken);
        return Result<string?>.Success(fileName);
    }
}

public sealed record SetGallerySampleAnchorCommand(Guid GalleryId, string? FileName) : ICommand;

public sealed class SetGallerySampleAnchorCommandHandler : ICommandHandler<SetGallerySampleAnchorCommand>
{
    private readonly IGalleryRepository _galleries;

    public SetGallerySampleAnchorCommandHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result> Handle(SetGallerySampleAnchorCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await _galleries.SetAnchorFileNameAsync(request.GalleryId, request.FileName, cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message);
        }
    }
}
