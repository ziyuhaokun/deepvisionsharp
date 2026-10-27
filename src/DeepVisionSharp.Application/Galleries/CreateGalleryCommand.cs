using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Galleries;

public sealed record CreateGalleryCommand(Guid ProjectId, string Name, string Description) : ICommand<ProjectGallery>;

public sealed class CreateGalleryCommandHandler : ICommandHandler<CreateGalleryCommand, ProjectGallery>
{
    private readonly IGalleryRepository _galleries;


    public CreateGalleryCommandHandler(IGalleryRepository galleries)
    {
        _galleries = galleries;
    }

    public async Task<Result<ProjectGallery>> Handle(CreateGalleryCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (await _galleries.ExistsByNameAsync(request.ProjectId, name, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return Result<ProjectGallery>.Failure($"图库名称「{name}」已存在。");
        }

        var gallery = await _galleries.CreateAsync(
            request.ProjectId,
            name,
            request.Description.Trim(),
            cancellationToken).ConfigureAwait(false);

        return Result<ProjectGallery>.Success(gallery);
    }
}
