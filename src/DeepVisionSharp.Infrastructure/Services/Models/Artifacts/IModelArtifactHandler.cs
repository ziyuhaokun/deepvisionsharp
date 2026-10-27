using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

internal interface IModelArtifactHandler
{
    string HandlerKey { get; }

    bool CanHandle(ModelArtifactContext context);

    Result<string> ResolveBestCheckpointPath(ModelArtifactContext context);

    Result<string> ResolveCheckpointPath(ModelArtifactContext context);

    Task<Result<IReadOnlyList<string>>> RecoverClassesAsync(
        ModelArtifactContext context,
        CancellationToken cancellationToken);

    Task<Result<SavedTrainedModelArtifact>> SaveAsync(
        ModelArtifactContext context,
        CancellationToken cancellationToken);
}
