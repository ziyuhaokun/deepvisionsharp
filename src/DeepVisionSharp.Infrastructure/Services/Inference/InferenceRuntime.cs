using System.Diagnostics;

namespace DeepVisionSharp.Infrastructure.Services;

/// <inheritdoc cref="IInferenceRuntime"/>
internal sealed class InferenceRuntime : IInferenceRuntime
{
    private readonly IInferenceEngineControl _engine;
    private readonly object _sync = new();
    private readonly Dictionary<long, Operation> _operations = new();
    private long _nextId;
    private InferenceRuntimeState _state = InferenceRuntimeState.Running;
    private Task? _shutdownTask;

    /// <summary>Completes when the registry is empty; replaced when the first operation registers.</summary>
    private TaskCompletionSource _idle = CompletedSignal();

    public InferenceRuntime(IInferenceEngineControl? engine = null)
    {
        _engine = engine ?? NullEngineControl.Instance;
    }

    public event Action<InferenceRuntimeState>? StateChanged;

    public InferenceRuntimeState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public int ActiveOperationCount
    {
        get
        {
            lock (_sync)
            {
                return _operations.Count;
            }
        }
    }

    public IReadOnlyList<string> ActiveOperations
    {
        get
        {
            lock (_sync)
            {
                return _operations.Values.Select(operation => operation.Kind).ToArray();
            }
        }
    }

    public IReadOnlyList<InferenceOperationInfo> OperationSnapshots
    {
        get
        {
            lock (_sync)
            {
                return _operations.Values
                    .Select(operation => new InferenceOperationInfo(operation.Kind, operation.OwnerKey, operation.IsCancelling))
                    .ToArray();
            }
        }
    }

    public Task<T> RunAsync<T>(
        string kind,
        Func<CancellationToken, T> work,
        CancellationToken cancellationToken,
        string? ownerKey = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        return ExecuteAsync(kind, token => Task.FromResult(work(token)), cancellationToken, ownerKey);
    }

    public Task<T> ExecuteAsync<T>(
        string kind,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken,
        string? ownerKey = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        long id;
        try
        {
            id = Register(kind, ownerKey, cancellation);
        }
        catch (Exception ex)
        {
            cancellation.Dispose();
            return Task.FromException<T>(ex);
        }

        var token = cancellation.Token;
        // The pool task deliberately gets no token: the finally block must always run to unregister.
        return Task.Run(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                return await work(token).ConfigureAwait(false);
            }
            finally
            {
                Unregister(id);
            }
        });
    }

    public Task ClearCacheAsync(CancellationToken cancellationToken)
        => RunAsync(InferenceOperationKinds.ClearCache, _ =>
        {
            _engine.ClearCache();
            return 0;
        }, cancellationToken);

    public async Task<IDisposable> BeginExclusiveAsync(TimeSpan drainTimeout, CancellationToken cancellationToken)
    {
        Task idle;
        lock (_sync)
        {
            switch (_state)
            {
                case InferenceRuntimeState.Exclusive:
                    throw new InvalidOperationException("已有训练任务正在占用推理引擎。");
                case InferenceRuntimeState.ShuttingDown:
                case InferenceRuntimeState.Stopped:
                    throw new InferenceRuntimeUnavailableException("推理运行时已关闭。");
            }

            // Flip immediately, like the device gate does: new operations must not slip in while
            // the registered ones drain.
            _state = InferenceRuntimeState.Exclusive;
            idle = _idle.Task;
        }

        RaiseStateChanged(InferenceRuntimeState.Exclusive);

        IDisposable? device = null;
        try
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await idle.WaitAsync(drainTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                throw new TimeoutException("等待推理结束后启动训练超时。", ex);
            }

            ThrowIfNotExclusive();

            var remaining = drainTimeout - watch.Elapsed;
            device = await _engine
                .BeginDeviceExclusiveAsync(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining, cancellationToken)
                .ConfigureAwait(false);
            ThrowIfNotExclusive();

            // The device is ours and no operation is registered, so nothing can repopulate the cache
            // between this clear and the trainer's first allocation.
            await Task.Run(_engine.ClearCache, CancellationToken.None).ConfigureAwait(false);
            ThrowIfNotExclusive();

            return new ExclusiveLease(this, device);
        }
        catch
        {
            device?.Dispose();
            EndExclusive();
            throw;
        }
    }

    private void ThrowIfNotExclusive()
    {
        lock (_sync)
        {
            if (_state != InferenceRuntimeState.Exclusive)
            {
                throw new InferenceRuntimeUnavailableException("推理运行时已关闭。");
            }
        }
    }

    private void EndExclusive()
    {
        var reopened = false;
        lock (_sync)
        {
            if (_state == InferenceRuntimeState.Exclusive)
            {
                _state = InferenceRuntimeState.Running;
                reopened = true;
            }
        }

        // After a shutdown the state stays as it is: a late lease dispose must not reopen the runtime.
        if (reopened)
        {
            RaiseStateChanged(InferenceRuntimeState.Running);
        }
    }

    public Task StopAsync(string? ownerKey, string? reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            // No identity, no scope: refusing to guess is what keeps one page's Stop from killing another's.
            return Task.CompletedTask;
        }

        List<Operation> owned;
        lock (_sync)
        {
            owned = _operations.Values
                .Where(operation => string.Equals(operation.OwnerKey, ownerKey, StringComparison.Ordinal))
                .ToList();
            foreach (var operation in owned)
            {
                operation.IsCancelling = true;
            }
        }

        if (owned.Count == 0)
        {
            return Task.CompletedTask;
        }

        CancelAll(owned.Select(operation => operation.Cancellation));
        return Task.WhenAll(owned.Select(operation => operation.Done.Task)).WaitAsync(cancellationToken);
    }

    public Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        Task drain;
        List<CancellationTokenSource>? toCancel = null;
        lock (_sync)
        {
            if (_shutdownTask is null)
            {
                _state = InferenceRuntimeState.ShuttingDown;
                toCancel = _operations.Values.Select(operation => operation.Cancellation).ToList();
                _shutdownTask = DrainThenStopAsync(_idle.Task);
            }

            drain = _shutdownTask;
        }

        if (toCancel is not null)
        {
            RaiseStateChanged(InferenceRuntimeState.ShuttingDown);
            CancelAll(toCancel);
        }

        return drain.WaitAsync(timeout, cancellationToken);
    }

    private async Task DrainThenStopAsync(Task idle)
    {
        await idle.ConfigureAwait(false);
        lock (_sync)
        {
            _state = InferenceRuntimeState.Stopped;
        }

        RaiseStateChanged(InferenceRuntimeState.Stopped);
    }

    private long Register(string kind, string? ownerKey, CancellationTokenSource cancellation)
    {
        lock (_sync)
        {
            switch (_state)
            {
                case InferenceRuntimeState.Exclusive:
                    throw new InferenceRuntimeUnavailableException("训练正在占用 GPU，请等待训练结束后再执行推理。");
                case InferenceRuntimeState.ShuttingDown:
                case InferenceRuntimeState.Stopped:
                    throw new InferenceRuntimeUnavailableException("推理运行时已关闭。");
            }

            if (_operations.Count == 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            var id = ++_nextId;
            _operations[id] = new Operation(kind, ownerKey, cancellation);
            return id;
        }
    }

    private void Unregister(long id)
    {
        TaskCompletionSource? idleToSignal = null;
        Operation? operation;
        lock (_sync)
        {
            if (!_operations.Remove(id, out operation))
            {
                return;
            }

            if (_operations.Count == 0)
            {
                idleToSignal = _idle;
            }
        }

        operation.Cancellation.Dispose();
        operation.Done.TrySetResult();
        idleToSignal?.TrySetResult();
    }

    private static void CancelAll(IEnumerable<CancellationTokenSource> sources)
    {
        foreach (var source in sources)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The operation completed between the snapshot and the cancel.
            }
        }
    }

    private void RaiseStateChanged(InferenceRuntimeState? state)
    {
        if (state is { } value)
        {
            StateChanged?.Invoke(value);
        }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private sealed class Operation(string kind, string? ownerKey, CancellationTokenSource cancellation)
    {
        public string Kind { get; } = kind;

        public string? OwnerKey { get; } = ownerKey;

        public CancellationTokenSource Cancellation { get; } = cancellation;

        /// <summary>Completes when the operation has unregistered, i.e. its work really ended.</summary>
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Guarded by the runtime lock.</summary>
        public bool IsCancelling { get; set; }
    }

    private sealed class ExclusiveLease(InferenceRuntime owner, IDisposable device) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            device.Dispose();
            owner.EndExclusive();
        }
    }
}
