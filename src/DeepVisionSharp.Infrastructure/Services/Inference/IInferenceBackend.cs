using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

internal interface IInferenceBackend
{
    string BackendKey { get; }

    bool CanHandle(InferenceBackendContext context);

    Task<InferenceResult> RunSingleAsync(
        InferenceBackendContext context,
        CancellationToken cancellationToken);

    /// <summary>Optional preflight+load. Default no-op success for backends without warm state.</summary>
    Task<InferenceWarmupResult> WarmupAsync(
        InferenceBackendContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(InferenceWarmupResult.Ok(0, 0, preflightCached: true, loadSkipped: true));
}
