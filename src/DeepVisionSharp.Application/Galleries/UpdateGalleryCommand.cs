using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public sealed record UpdateGalleryCommand(Guid Id, string Name, string Description) : ICommand<ProjectGallery>;

public sealed class UpdateGalleryCommandHandler : ICommandHandler<UpdateGalleryCommand, ProjectGallery>
{
    private readonly IGalleryRepository _galleries;

    public UpdateGalleryCommandHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<ProjectGallery>> Handle(UpdateGalleryCommand request, CancellationToken cancellationToken)
    {
        var gallery = await _galleries.GetSummaryByIdAsync(request.Id, cancellationToken).ConfigureAwait(false);
        if (gallery is null)
        {
            return Result<ProjectGallery>.Failure("图库不存在");
        }

        var name = request.Name.Trim();
        if (await _galleries.ExistsByNameAsync(gallery.ProjectId, name, gallery.Id, cancellationToken).ConfigureAwait(false))
        {
            return Result<ProjectGallery>.Failure($"图库名称「{name}」已存在。");
        }

        var updated = gallery.WithDetails(name, request.Description.Trim());
        await _galleries.UpdateDetailsAsync(
            request.Id,
            name,
            request.Description.Trim(),
            cancellationToken).ConfigureAwait(false);
        return Result<ProjectGallery>.Success(updated);
    }
}
