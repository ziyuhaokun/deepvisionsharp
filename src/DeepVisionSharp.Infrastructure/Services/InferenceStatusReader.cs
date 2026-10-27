using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class InferenceStatusReader : IInferenceStatusReader
{
    private readonly InferenceBackendRegistry _backendRegistry;

    public InferenceStatusReader(InferenceBackendRegistry backendRegistry)
    {
        _backendRegistry = backendRegistry;
    }

    public bool IsInferenceRunning => _backendRegistry.Backends.Any(backend => backend.IsActive);
}
