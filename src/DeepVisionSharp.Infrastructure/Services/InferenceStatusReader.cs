using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>Reports whether an inference run is registered in the runtime (warmup and cache clears do not count).</summary>
internal sealed class InferenceStatusReader : IInferenceStatusReader
{
    private readonly IInferenceRuntime _runtime;

    public InferenceStatusReader(IInferenceRuntime runtime)
    {
        _runtime = runtime;
    }

    public bool IsInferenceRunning => _runtime.OperationSnapshots
        .Any(operation => operation.Kind == InferenceOperationKinds.Inference);
}
