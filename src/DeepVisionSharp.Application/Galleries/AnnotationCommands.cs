using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Galleries;

public sealed record SaveAnnotationsCommand(
    Guid GalleryId,
    string FileName,
    double ImageWidth,
    double ImageHeight,
    string? ImageClass = null) : ICommand;

public sealed class SaveAnnotationsCommandHandler : ICommandHandler<SaveAnnotationsCommand>
{
    private readonly IAnnotationWorkflowService _annotations;

    public SaveAnnotationsCommandHandler(IAnnotationWorkflowService annotations)
    {
        _annotations = annotations;
    }

    public async Task<Result> Handle(SaveAnnotationsCommand request, CancellationToken cancellationToken)
    {
        await _annotations.SaveAsync(
            request.GalleryId,
            request.FileName,
            request.ImageWidth,
            request.ImageHeight,
            request.ImageClass,
            cancellationToken);

        return Result.Success();
    }
}

public sealed record DeleteSampleCommand(Guid GalleryId, string FileName) : ICommand;

public sealed class DeleteSampleCommandHandler : ICommandHandler<DeleteSampleCommand>
{
    private readonly IAnnotationWorkflowService _annotations;

    public DeleteSampleCommandHandler(IAnnotationWorkflowService annotations)
    {
        _annotations = annotations;
    }

    public async Task<Result> Handle(DeleteSampleCommand request, CancellationToken cancellationToken)
    {
        await _annotations.DeleteSampleAsync(request.GalleryId, request.FileName, cancellationToken);
        return Result.Success();
    }
}
