using System.Diagnostics;

using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceShutdownServiceTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Shutdown_stops_runtime_then_disposes_engine_once()
    {
        var runtime = new InferenceRuntime();
        var engine = new FakeEngine();
        var service = new InferenceShutdownService(runtime, engine, Wait, Wait);

        await service.StopAndDisposeAsync("exit");
        await service.StopAndDisposeAsync("exit again");

        Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);
        Assert.Equal(1, engine.DisposeCount);
    }

    [Fact]
    public async Task Shutdown_cancels_cooperative_operations_before_disposing_the_engine()
    {
        var runtime = new InferenceRuntime();
        var engine = new FakeEngine();
        var entered = new TaskCompletionSource();
        var operation = runtime.RunAsync<int>("inference", token =>
        {
            entered.SetResult();
            token.WaitHandle.WaitOne(Wait);
            Assert.Equal(0, engine.DisposeCount); // the engine must outlive the operation
            token.ThrowIfCancellationRequested();
            return 1;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(Wait);
        var service = new InferenceShutdownService(runtime, engine, Wait, Wait);

        await service.StopAndDisposeAsync("exit");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, engine.DisposeCount);
    }

    [Fact]
    public async Task Shutdown_reports_timeout_when_native_work_does_not_drain_but_still_attempts_dispose()
    {
        var runtime = new InferenceRuntime();
        var engine = new FakeEngine();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var operation = runtime.RunAsync("inference", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(Wait);
        var service = new InferenceShutdownService(runtime, engine, Short, Wait);

        await Assert.ThrowsAsync<TimeoutException>(() => service.StopAndDisposeAsync("exit"));

        Assert.Equal(1, engine.DisposeCount);
        release.Set();
        await operation.WaitAsync(Wait);
    }

    [Fact]
    public async Task Shutdown_is_bounded_when_engine_dispose_blocks_and_does_not_hold_the_caller_thread()
    {
        var runtime = new InferenceRuntime();
        using var unblock = new ManualResetEventSlim();
        var engine = new FakeEngine { DisposeBlock = unblock };
        var service = new InferenceShutdownService(runtime, engine, Wait, Short);
        var watch = Stopwatch.StartNew();

        var call = service.StopAndDisposeAsync("exit"); // must return its Task promptly
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));

        await Assert.ThrowsAsync<TimeoutException>(() => call);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4));

        unblock.Set();
    }

    private sealed class FakeEngine : IDisposable
    {
        private int _disposeCount;

        public ManualResetEventSlim? DisposeBlock { get; init; }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            DisposeBlock?.Wait();
        }
    }
}
