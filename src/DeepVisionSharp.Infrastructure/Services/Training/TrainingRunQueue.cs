using System.Threading.Channels;

using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// A prepared training run waiting for <see cref="TrainingRunWorkerHostedService"/>.
/// The gate lease is owned by the worker for the whole run so project deletion stays blocked
/// after the start command has returned.
/// </summary>
internal sealed record TrainingRunRequest(
    Guid JobId,
    Guid ProjectId,
    TrainingStartContext Context,
    ITrainingBackend Backend,
    IDisposable GateLease);

/// <summary>A queue item plus the token the worker should observe.</summary>
internal sealed record TrainingRunWorkItem(TrainingRunRequest Request, CancellationToken Token);

/// <summary>
/// Single-flight hand-off between the start command and the background worker.
/// </summary>
/// <remarks>
/// The command prepares the job and returns; dataset export, training and checkpoint saving happen
/// here. One run at a time, matching the engine's single-flight rule (one GPU, one native runtime).
/// </remarks>
internal sealed class TrainingRunQueue
{
    private readonly Channel<TrainingRunWorkItem> _channel = Channel.CreateUnbounded<TrainingRunWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly object _sync = new();
    private readonly Dictionary<Guid, CancellationTokenSource> _cancellations = new();

    /// <summary>True while any run is queued or executing.</summary>
    public bool HasAny
    {
        get
        {
            lock (_sync)
            {
                return _cancellations.Count > 0;
            }
        }
    }

    /// <summary>True while a run is queued or executing; used by the orphan reconciler.</summary>
    public bool IsTracked(Guid jobId)
    {
        lock (_sync)
        {
            return _cancellations.ContainsKey(jobId);
        }
    }

    public bool TryEnqueue(TrainingRunRequest request, out string? error)
    {
        lock (_sync)
        {
            if (_cancellations.Count > 0)
            {
                error = "已有训练任务正在运行，请等待其完成或停止后再启动。";
                return false;
            }

            var cancellation = new CancellationTokenSource();
            _cancellations[request.JobId] = cancellation;
            if (!_channel.Writer.TryWrite(new TrainingRunWorkItem(request, cancellation.Token)))
            {
                _cancellations.Remove(request.JobId);
                cancellation.Dispose();
                error = "训练队列不可用，请重启应用后重试。";
                return false;
            }

            error = null;
            return true;
        }
    }

    public IAsyncEnumerable<TrainingRunWorkItem> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <summary>Cancels a queued or executing job. Returns false when the job is not tracked.</summary>
    public bool TryCancel(Guid jobId)
    {
        lock (_sync)
        {
            if (!_cancellations.TryGetValue(jobId, out var cancellation))
            {
                return false;
            }

            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The worker completed the run between the lookup and the cancel.
            }

            return true;
        }
    }

    /// <summary>Removes a finished job and disposes its token source.</summary>
    public void Complete(Guid jobId)
    {
        lock (_sync)
        {
            if (_cancellations.Remove(jobId, out var cancellation))
            {
                cancellation.Dispose();
            }
        }
    }
}
