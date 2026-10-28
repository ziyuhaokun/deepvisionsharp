using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceRuntimeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Run_registers_operation_while_executing_and_releases_it_on_completion()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();

        var run = runtime.RunAsync("infer", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 42;
        }, CancellationToken.None);

        await entered.Task.WaitAsync(Wait);
        Assert.Equal(1, runtime.ActiveOperationCount);
        Assert.Equal(["infer"], runtime.ActiveOperations);

        release.Set();
        Assert.Equal(42, await run.WaitAsync(Wait));
        Assert.Equal(0, runtime.ActiveOperationCount);
        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
    }

    [Fact]
    public async Task Run_releases_registration_when_work_throws()
    {
        var runtime = new InferenceRuntime();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.RunAsync<int>("infer", _ => throw new InvalidOperationException("boom"), CancellationToken.None));

        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task Run_with_already_cancelled_token_does_not_start_work()
    {
        var runtime = new InferenceRuntime();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var invoked = 0;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.RunAsync("infer", _ => Interlocked.Increment(ref invoked), cts.Token));

        Assert.Equal(0, invoked);
        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task Owner_stop_cancels_token_of_cooperative_operation()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();

        var run = runtime.RunAsync<int>("warmup", token =>
        {
            entered.SetResult();
            token.WaitHandle.WaitOne(Wait);
            token.ThrowIfCancellationRequested();
            return 1;
        }, CancellationToken.None, "page");

        await entered.Task.WaitAsync(Wait);
        await runtime.StopAsync("page", "user", CancellationToken.None).WaitAsync(Wait);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, runtime.ActiveOperationCount);
        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
    }

    [Fact]
    public async Task Owner_stop_marks_operation_cancelling_without_changing_runtime_state()
    {
        var runtime = new InferenceRuntime();
        var states = new List<InferenceRuntimeState>();
        runtime.StateChanged += state =>
        {
            lock (states)
            {
                states.Add(state);
            }
        };
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var seen = default(CancellationToken);

        var run = runtime.RunAsync("infer", token =>
        {
            seen = token;
            entered.SetResult();
            release.Wait(); // simulates a native forward that ignores the token
            return 7;
        }, CancellationToken.None, "page");

        await entered.Task.WaitAsync(Wait);
        var stop = runtime.StopAsync("page", "user", CancellationToken.None);

        var info = Assert.Single(runtime.OperationSnapshots);
        Assert.Equal(new InferenceOperationInfo("infer", "page", IsCancelling: true), info);
        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
        Assert.False(stop.IsCompleted);
        Assert.True(seen.IsCancellationRequested);

        release.Set();
        await stop.WaitAsync(Wait);

        // The native call was not interrupted, so it completes normally rather than pretending to cancel.
        Assert.Equal(7, await run.WaitAsync(Wait));
        Assert.Empty(runtime.OperationSnapshots);
        lock (states)
        {
            Assert.Empty(states);
        }
    }

    [Fact]
    public async Task Shutdown_rejects_new_work_and_ends_stopped()
    {
        var runtime = new InferenceRuntime();

        await runtime.ShutdownAsync(Wait, CancellationToken.None);

        Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);
        await Assert.ThrowsAsync<InferenceRuntimeUnavailableException>(() =>
            runtime.RunAsync("infer", _ => 1, CancellationToken.None));
    }

    [Fact]
    public async Task Shutdown_is_idempotent_including_concurrent_callers()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var run = runtime.RunAsync("infer", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        var first = runtime.ShutdownAsync(Wait, CancellationToken.None);
        var second = runtime.ShutdownAsync(Wait, CancellationToken.None);
        Assert.Equal(InferenceRuntimeState.ShuttingDown, runtime.State);

        release.Set();
        await Task.WhenAll(first, second).WaitAsync(Wait);
        await run.WaitAsync(Wait);
        await runtime.ShutdownAsync(Wait, CancellationToken.None);

        Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);
        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task Shutdown_times_out_while_native_work_runs_and_can_be_retried_to_completion()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var seen = default(CancellationToken);
        var run = runtime.RunAsync("infer", token =>
        {
            seen = token;
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runtime.ShutdownAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None));

        // A timed-out shutdown must not silently report Stopped while native code still runs.
        Assert.Equal(InferenceRuntimeState.ShuttingDown, runtime.State);
        Assert.True(seen.IsCancellationRequested);

        release.Set();
        await run.WaitAsync(Wait);
        await runtime.ShutdownAsync(Wait, CancellationToken.None);
        Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);
    }

    [Fact]
    public async Task Run_racing_with_shutdown_never_leaves_work_running_after_stopped()
    {
        for (var round = 0; round < 200; round++)
        {
            var runtime = new InferenceRuntime();
            var running = 0;
            var startedAfterStopped = 0;
            using var start = new ManualResetEventSlim();

            var runs = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
            {
                start.Wait();
                try
                {
                    await runtime.RunAsync("infer", _ =>
                    {
                        Interlocked.Increment(ref running);
                        if (runtime.State == InferenceRuntimeState.Stopped)
                        {
                            Interlocked.Increment(ref startedAfterStopped);
                        }

                        Thread.Sleep(1);
                        Interlocked.Decrement(ref running);
                        return 0;
                    }, CancellationToken.None);
                }
                catch (InferenceRuntimeUnavailableException)
                {
                }
                catch (OperationCanceledException)
                {
                }
            })).ToArray();
            var shutdown = Task.Run(async () =>
            {
                start.Wait();
                await runtime.ShutdownAsync(Wait, CancellationToken.None);
            });

            start.Set();
            await shutdown;

            Assert.Equal(0, Volatile.Read(ref running));
            Assert.Equal(0, runtime.ActiveOperationCount);
            Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);
            await Task.WhenAll(runs).WaitAsync(Wait);
            Assert.Equal(0, startedAfterStopped);
        }
    }
}
