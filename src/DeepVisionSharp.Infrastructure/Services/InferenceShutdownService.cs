using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.TorchSharp.Classification;

namespace DeepVisionSharp.Infrastructure.Services;

public sealed class InferenceShutdownService : IInferenceShutdown
{
    private readonly TorchSharpClsEngine _clsEngine;

    public InferenceShutdownService(TorchSharpClsEngine clsEngine)
    {
        _clsEngine = clsEngine;
    }

    public Task StopAndDisposeAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        _clsEngine.Dispose();
        return Task.CompletedTask;
    }
}
