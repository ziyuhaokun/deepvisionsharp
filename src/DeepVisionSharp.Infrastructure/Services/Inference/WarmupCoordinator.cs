using System.Diagnostics;

using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

/// <inheritdoc cref="IWarmupCoordinator"/>
internal sealed class WarmupCoordinator(IInferenceRunner runner) : IWarmupCoordinator
{
    private readonly object _sync = new();
    private WarmupRequest? _pending;
    private string? _desiredKey;
    private CancellationTokenSource? _current;
    private string? _currentKey;
    private Task? _loop;

    public event Action<WarmupOutcome>? Completed;

    public bool IsRunning
    {
        get
        {
            lock (_sync)
            {
                return _current is not null;
            }
        }
    }

    public string? DesiredKey
    {
        get
        {
            lock (_sync)
            {
                return _desiredKey;
            }
        }
    }

    public void Request(WarmupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        CancellationTokenSource? stale = null;
        lock (_sync)
        {
            if (_current is not null && _currentKey == request.Key)
            {
                // Already loading exactly this: nothing to queue.
                _pending = null;
                _desiredKey = request.Key;
                return;
            }

            if (_current is null && _pending?.Key == request.Key)
            {
                _desiredKey = request.Key;
                EnsureLoopNoLock();
                return;
            }

            _pending = request;
            _desiredKey = request.Key;
            if (_current is not null && _currentKey != request.Key)
            {
                stale = _current;
            }

            EnsureLoopNoLock();
        }

        // Asked to stop, but the new target still waits for this call to return (see RunLoopAsync).
        CancelQuietly(stale);
    }

    public void Cancel()
    {
        CancellationTokenSource? current;
        lock (_sync)
        {
            _pending = null;
            _desiredKey = null;
            current = _current;
        }

        CancelQuietly(current);
    }

    public async Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            Task? loop;
            lock (_sync)
            {
                loop = _loop;
            }

            if (loop is null)
            {
                return true;
            }

            var remaining = timeout - watch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }

            try
            {
                await loop.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return false;
            }
        }
    }

    /// <summary>Must be called while holding <see cref="_sync"/>.</summary>
    private void EnsureLoopNoLock()
    {
        // Holding the lock makes this safe: the new loop cannot clear _loop before the assignment
        // below runs, because it needs the same lock to do so.
        _loop ??= Task.Run(RunLoopAsync);
    }

    private async Task RunLoopAsync()
    {
        while (true)
        {
            WarmupRequest next;
            CancellationTokenSource cancellation;
            lock (_sync)
            {
                if (_pending is null)
                {
                    _loop = null;
                    return;
                }

                next = _pending;
                _pending = null;
                cancellation = new CancellationTokenSource();
                _current = cancellation;
                _currentKey = next.Key;
            }

            WarmupOutcome outcome;
            try
            {
                outcome = await RunOnceAsync(next, cancellation.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_current, cancellation))
                    {
                        _current = null;
                        _currentKey = null;
                    }
                }

                // Safe to dispose here: this attempt's call has returned and nothing else holds it.
                cancellation.Dispose();
            }

            try
            {
                Completed?.Invoke(outcome);
            }
            catch
            {
                // A subscriber failure must not end the loop and strand later targets.
            }
        }
    }

    private async Task<WarmupOutcome> RunOnceAsync(WarmupRequest request, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await runner
                .WarmupAsync(request.ModelPath, request.Config, request.ModelType, token)
                .ConfigureAwait(false);
            return new WarmupOutcome(request, result.Success, Cancelled: false, result.Error, watch.Elapsed);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return new WarmupOutcome(request, Success: false, Cancelled: true, Error: null, watch.Elapsed);
        }
        catch (Exception ex)
        {
            return new WarmupOutcome(request, Success: false, Cancelled: false, ex.Message, watch.Elapsed);
        }
    }

    private static void CancelQuietly(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt ended between the snapshot and the cancel.
        }
    }
}
