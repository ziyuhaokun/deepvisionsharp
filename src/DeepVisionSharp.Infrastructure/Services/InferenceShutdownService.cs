using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Application-exit shutdown of the in-process inference engine: drains registered operations through
/// <see cref="IInferenceRuntime"/>, then disposes the engine.
/// </summary>
/// <remarks>
/// <c>Dispose</c> on the engine takes a write lock that waits for every in-flight native call, so it
/// runs on the thread pool with a bound instead of on the UI thread that is exiting. A timeout is
/// reported as <see cref="TimeoutException"/> after both stages were attempted; the process is
/// exiting anyway, so the caller logs it and carries on.
/// </remarks>
internal sealed class InferenceShutdownService : IInferenceShutdown
{
    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultDisposeTimeout = TimeSpan.FromSeconds(2);

    private readonly IInferenceRuntime _runtime;
    private readonly IDisposable _engine;
    private readonly TimeSpan _drainTimeout;
    private readonly TimeSpan _disposeTimeout;
    private readonly object _sync = new();
    private Task? _disposeTask;

    public InferenceShutdownService(
        IInferenceRuntime runtime,
        IDisposable engine,
        TimeSpan? drainTimeout = null,
        TimeSpan? disposeTimeout = null)
    {
        _runtime = runtime;
        _engine = engine;
        _drainTimeout = drainTimeout ?? DefaultDrainTimeout;
        _disposeTimeout = disposeTimeout ?? DefaultDisposeTimeout;
    }

    public async Task StopAndDisposeAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        TimeoutException? timeout = null;

        try
        {
            await _runtime.ShutdownAsync(_drainTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            // Native work is still running. Dispose below will wait for it on a pool thread.
            timeout = ex;
        }

        try
        {
            // One dispose per engine: a repeated or retried shutdown shares it instead of parking
            // another pool thread on the engine's lifetime lock.
            Task dispose;
            lock (_sync)
            {
                dispose = _disposeTask ??= Task.Run(_engine.Dispose, CancellationToken.None);
            }

            await dispose.WaitAsync(_disposeTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            timeout ??= ex;
        }

        if (timeout is not null)
        {
            throw new TimeoutException("推理引擎未能在限定时间内完全关闭，进程将继续退出。", timeout);
        }
    }
}
