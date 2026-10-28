using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class TrainingRunQueueTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public void Shutdown_before_dequeue_releases_pending_run_and_tracking()
    {
        var queue = new TrainingRunQueue();
        var lease = new TrackingLease();
        var jobId = Guid.NewGuid();
        var request = new TrainingRunRequest(
            jobId,
            Guid.NewGuid(),
            Context: null!,
            Backend: null!,
            GateLease: lease);

        Assert.True(queue.TryEnqueue(request, out var error), error);
        Assert.True(queue.HasAny);
        Assert.True(queue.IsTracked(jobId));

        var pending = queue.Shutdown();

        var returned = Assert.Single(pending);
        Assert.Equal(jobId, returned.JobId);
        Assert.Equal(1, lease.DisposeCount);
        Assert.False(queue.HasAny);
        Assert.False(queue.IsTracked(jobId));
        Assert.False(queue.TryEnqueue(request, out _));
    }

    [Fact]
    public void Shutdown_twice_is_idempotent_and_does_not_double_dispose()
    {
        var queue = new TrainingRunQueue();
        var lease = new TrackingLease();
        Assert.True(queue.TryEnqueue(NewRequest(lease), out var error), error);

        Assert.Single(queue.Shutdown());
        Assert.Empty(queue.Shutdown());
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task Shutdown_cancels_item_already_taken_by_worker_and_keeps_it_tracked_until_complete()
    {
        var queue = new TrainingRunQueue();
        var lease = new TrackingLease();
        var request = NewRequest(lease);
        Assert.True(queue.TryEnqueue(request, out var error), error);

        TrainingRunWorkItem? taken = null;
        await foreach (var work in queue.ReadAllAsync(CancellationToken.None))
        {
            taken = work;
            break;
        }

        Assert.NotNull(taken);
        Assert.False(taken.Token.IsCancellationRequested);

        var pending = queue.Shutdown();

        Assert.Empty(pending);
        Assert.True(taken.Token.IsCancellationRequested);
        // The worker still owns the lease and the tracking entry for the item it already took.
        Assert.Equal(0, lease.DisposeCount);
        Assert.True(queue.IsTracked(request.JobId));

        queue.Complete(request.JobId);
        Assert.False(queue.HasAny);
    }

    [Fact]
    public async Task Shutdown_ends_worker_read_loop_without_hanging()
    {
        var queue = new TrainingRunQueue();
        var readerStarted = new TaskCompletionSource();
        var reader = Task.Run(async () =>
        {
            readerStarted.TrySetResult();
            await foreach (var _ in queue.ReadAllAsync(CancellationToken.None))
            {
            }
        });

        // Wait until the loop is actually awaiting a read, so the shutdown below races a live
        // consumer rather than a task that has not started yet.
        await readerStarted.Task.WaitAsync(Wait);
        queue.Shutdown();

        await reader.WaitAsync(Wait);
    }

    [Fact]
    public async Task Shutdown_racing_with_worker_read_hands_item_to_exactly_one_owner()
    {
        for (var round = 0; round < 200; round++)
        {
            var queue = new TrainingRunQueue();
            var lease = new TrackingLease();
            var request = NewRequest(lease);
            Assert.True(queue.TryEnqueue(request, out var error), error);

            var taken = 0;
            using var start = new ManualResetEventSlim();
            var reader = Task.Run(async () =>
            {
                start.Wait();
                await foreach (var work in queue.ReadAllAsync(CancellationToken.None))
                {
                    Interlocked.Increment(ref taken);
                    work.Request.GateLease.Dispose();
                    queue.Complete(work.Request.JobId);
                }
            });
            var shutdown = Task.Run(() =>
            {
                start.Wait();
                return queue.Shutdown();
            });

            start.Set();
            var pending = await shutdown;
            await reader.WaitAsync(TimeSpan.FromSeconds(2));

            // Either the worker or Shutdown owned the item, never both and never neither.
            Assert.Equal(1, taken + pending.Count);
            Assert.Equal(1, lease.DisposeCount);
            Assert.False(queue.HasAny);
        }
    }

    [Fact]
    public async Task Enqueue_racing_with_shutdown_never_leaves_an_orphaned_item()
    {
        for (var round = 0; round < 200; round++)
        {
            var queue = new TrainingRunQueue();
            var lease = new TrackingLease();
            var request = NewRequest(lease);
            using var start = new ManualResetEventSlim();

            var enqueue = Task.Run(() =>
            {
                start.Wait();
                return queue.TryEnqueue(request, out _);
            });
            var shutdown = Task.Run(() =>
            {
                start.Wait();
                return queue.Shutdown();
            });

            start.Set();
            var accepted = await enqueue;
            var pending = await shutdown;

            // Accepted before shutdown -> drained; rejected -> nothing tracked and lease untouched.
            // Shutdown may also run first and then reject, so only the invariants are asserted.
            if (accepted)
            {
                Assert.Single(pending);
                Assert.Equal(1, lease.DisposeCount);
            }
            else
            {
                Assert.Empty(pending);
                Assert.Equal(0, lease.DisposeCount);
            }

            Assert.False(queue.HasAny);
        }
    }

    private static TrainingRunRequest NewRequest(IDisposable lease)
        => new(Guid.NewGuid(), Guid.NewGuid(), Context: null!, Backend: null!, GateLease: lease);

    private sealed class TrackingLease : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
