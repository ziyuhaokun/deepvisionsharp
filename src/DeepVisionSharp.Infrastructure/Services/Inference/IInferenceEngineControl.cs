using DeepVisionSharp.TorchSharp.Classification;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// The slice of the native inference engine that <see cref="IInferenceRuntime"/> drives. Keeping it
/// narrow lets the runtime's exclusivity protocol be tested without loading native libraries.
/// </summary>
internal interface IInferenceEngineControl
{
    /// <summary>
    /// Takes the engine's device gate, waiting for inference that entered through the engine directly.
    /// </summary>
    Task<IDisposable> BeginDeviceExclusiveAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Releases cached native models and returns freed device memory to the driver.</summary>
    void ClearCache();
}

/// <summary>Drives the in-process TorchSharp classification engine.</summary>
internal sealed class TorchSharpEngineControl(TorchSharpClsEngine engine) : IInferenceEngineControl
{
    public Task<IDisposable> BeginDeviceExclusiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => engine.Gate.BeginTrainingAsync(timeout, cancellationToken);

    public void ClearCache() => engine.ClearCache();
}

/// <summary>Engine control for a runtime that owns no native engine (no device to protect).</summary>
internal sealed class NullEngineControl : IInferenceEngineControl
{
    public static readonly NullEngineControl Instance = new();

    public Task<IDisposable> BeginDeviceExclusiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
        => Task.FromResult<IDisposable>(new Noop());

    public void ClearCache()
    {
    }

    private sealed class Noop : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
