namespace DeepVisionSharp.TorchSharp.Device;

/// <summary>
/// Serialises in-process training against inference so the two never share the GPU at once.
/// </summary>
/// <remarks>
/// Training and inference run in the same process and the same CUDA context. Letting them overlap
/// means two models resident on one device, which fails unpredictably once memory runs out. The gate
/// is deliberately asymmetric:
/// <list type="bullet">
/// <item>Training start waits for in-flight inference to drain (a bounded, short wait).</item>
/// <item>Inference started while training is active fails fast with a clear message instead of
/// blocking the UI thread for the whole training run.</item>
/// </list>
/// </remarks>
public sealed class InferenceTrainingGate
{
    private readonly object _sync = new();
    private readonly List<TaskCompletionSource> _drainWaiters = new();
    private int _inFlightInference;
    private bool _trainingActive;

    /// <summary>True while a training run holds the gate.</summary>
    public bool IsTrainingActive
    {
        get
        {
            lock (_sync)
            {
                return _trainingActive;
            }
        }
    }

    /// <summary>Number of inference calls currently executing.</summary>
    public int InFlightInference
    {
        get
        {
            lock (_sync)
            {
                return _inFlightInference;
            }
        }
    }

    /// <summary>
    /// Marks training as active and waits for running inference calls to finish.
    /// </summary>
    /// <returns>A lease that clears the flag when disposed.</returns>
    /// <exception cref="InvalidOperationException">Another training run already holds the gate.</exception>
    public async Task<IDisposable> BeginTrainingAsync(
        TimeSpan drainTimeout,
        CancellationToken cancellationToken = default)
    {
        Task drainTask;
        lock (_sync)
        {
            if (_trainingActive)
            {
                throw new InvalidOperationException("已有训练任务正在占用推理引擎。");
            }

            _trainingActive = true;
            if (_inFlightInference == 0)
            {
                return new TrainingLease(this);
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _drainWaiters.Add(waiter);
            drainTask = waiter.Task;
        }

        try
        {
            // Training and inference share one native device. Starting training while an inference
            // call is still in native code can exceed GPU memory or race the native runtime, so a
            // drain timeout is a failed hand-off rather than permission to overlap the operations.
            await drainTask.WaitAsync(drainTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            CancelTrainingWaiter(drainTask);
            throw new TimeoutException("等待推理结束后启动训练超时。", ex);
        }
        catch
        {
            // Cancelled or failed while draining: give the gate back so training is not wedged.
            CancelTrainingWaiter(drainTask);
            throw;
        }

        return new TrainingLease(this);
    }

    /// <summary>
    /// Registers an inference call. Throws when training holds the gate so the caller reports a
    /// clear "training in progress" error instead of hanging.
    /// </summary>
    public IDisposable EnterInference()
    {
        lock (_sync)
        {
            if (_trainingActive)
            {
                throw new InferenceBlockedByTrainingException();
            }

            _inFlightInference++;
        }

        return new InferenceLease(this);
    }

    private void ExitInference()
    {
        List<TaskCompletionSource>? toRelease = null;
        lock (_sync)
        {
            if (_inFlightInference > 0)
            {
                _inFlightInference--;
            }

            if (_inFlightInference == 0 && _drainWaiters.Count > 0)
            {
                toRelease = new List<TaskCompletionSource>(_drainWaiters);
                _drainWaiters.Clear();
            }
        }

        if (toRelease is not null)
        {
            foreach (var waiter in toRelease)
            {
                waiter.TrySetResult();
            }
        }
    }

    private void CancelTrainingWaiter(Task drainTask)
    {
        lock (_sync)
        {
            _trainingActive = false;
            _drainWaiters.RemoveAll(waiter => waiter.Task == drainTask);
        }
    }

    private void EndTraining()
    {
        lock (_sync)
        {
            _trainingActive = false;
        }
    }

    private sealed class InferenceLease(InferenceTrainingGate owner) : IDisposable
    {
        private InferenceTrainingGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ExitInference();
    }

    private sealed class TrainingLease(InferenceTrainingGate owner) : IDisposable
    {
        private InferenceTrainingGate? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndTraining();
    }
}

/// <summary>Raised when inference is attempted while a training run holds the gate.</summary>
public sealed class InferenceBlockedByTrainingException()
    : InvalidOperationException("训练正在占用 GPU，请等待训练结束后再执行推理。");
