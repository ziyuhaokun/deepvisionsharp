using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public sealed record DeleteGalleryCommand(Guid GalleryId) : ICommand;

public sealed class DeleteGalleryCommandHandler : ICommandHandler<DeleteGalleryCommand>
{
    private readonly IGalleryRepository _galleries;

    public DeleteGalleryCommandHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result> Handle(DeleteGalleryCommand request, CancellationToken cancellationToken)
    {
        var gallery = await _galleries.GetSummaryByIdAsync(request.GalleryId, cancellationToken).ConfigureAwait(false);
        if (gallery is null)
        {
            return Result.Failure("图库不存在或删除失败");
        }

        var deleted = await _galleries.DeleteAsync(request.GalleryId, cancellationToken).ConfigureAwait(false);
        return deleted
            ? Result.Success()
            : Result.Failure("图库不存在或删除失败");
    }
}
