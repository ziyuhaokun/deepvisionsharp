namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// Releases native model memory held by the in-process inference engine without disabling it.
/// </summary>
/// <remarks>
/// Inference keeps loaded models resident because loading is expensive. Training runs in the same
/// process and shares the GPU, so a start path can drop those models first. Implementations must
/// keep models that are currently in use alive until their inference returns.
/// </remarks>
public interface IInferenceModelCache
{
    void Clear();
}
