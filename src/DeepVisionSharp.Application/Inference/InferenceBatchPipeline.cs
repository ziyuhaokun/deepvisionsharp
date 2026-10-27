using System.Threading.Channels;

namespace DeepVisionSharp.Application.Inference;

/// <summary>
/// High-performance producer-consumer batch inference processing pipeline using System.Threading.Channels.
/// Decouples item queuing, prefetching, and execution with configurable concurrency.
/// </summary>
public static class InferenceBatchPipeline
{
    /// <summary>
    /// Processes every item, or throws <see cref="OperationCanceledException"/> when
    /// <paramref name="cancellationToken"/> fires. Callers must treat the exception as "stopped
    /// early": a silent return would let them report a partial batch as complete.
    /// </summary>
    public static async Task ExecuteAsync<T>(
        IReadOnlyList<T> items,
        Func<T, CancellationToken, Task> processItemAsync,
        int maxConcurrency = 1,
        Action<int, int, T>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            return;
        }

        var total = items.Count;
        var processedCount = 0;
        var channel = Channel.CreateUnbounded<T>(new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = maxConcurrency == 1
        });

        // 1. Producer: Publish items to channel
        var producerTask = Task.Run(async () =>
        {
            try
            {
                foreach (var item in items)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        // 2. Consumer workers: Read items from channel and process
        var workerCount = Math.Max(1, maxConcurrency);
        var workers = new Task[workerCount];

        for (var i = 0; i < workerCount; i++)
        {
            workers[i] = Task.Run(async () =>
            {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    await processItemAsync(item, cancellationToken).ConfigureAwait(false);

                    var currentProcessed = Interlocked.Increment(ref processedCount);
                    onProgress?.Invoke(currentProcessed, total, item);
                }
            }, cancellationToken);
        }

        try
        {
            await Task.WhenAll(workers.Append(producerTask)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Surface cancellation instead of returning normally: the caller has no other way to
            // tell a fully processed batch from one that stopped partway.
            throw;
        }
    }
}
