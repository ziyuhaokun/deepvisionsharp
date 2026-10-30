using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Detection;

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

/// <summary>
/// Gates both in-process engines at once. Training holds one GPU, so a training run must wait for
/// classification *and* detection inference, and clearing the cache must release both model sets.
/// </summary>
internal sealed class CompositeTorchSharpEngineControl(
    TorchSharpClsEngine classification,
    TorchSharpDetectEngine detection) : IInferenceEngineControl
{
    public async Task<IDisposable> BeginDeviceExclusiveAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var first = await classification.Gate.BeginTrainingAsync(timeout, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var second = await detection.Gate.BeginTrainingAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            return new Both(first, second);
        }
        catch
        {
            first.Dispose();
            throw;
        }
    }

    public void ClearCache()
    {
        classification.ClearCache();
        detection.ClearCache();
    }

    private sealed class Both(IDisposable first, IDisposable second) : IDisposable
    {
        public void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }
    }
}

/// <summary>Disposes both native engines at application exit.</summary>
internal sealed class CompositeInferenceEngine(
    TorchSharpClsEngine classification,
    TorchSharpDetectEngine detection) : IDisposable
{
    public void Dispose()
    {
        classification.Dispose();
        detection.Dispose();
    }
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
