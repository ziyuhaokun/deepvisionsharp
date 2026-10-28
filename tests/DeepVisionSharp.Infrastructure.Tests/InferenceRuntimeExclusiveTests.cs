using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceRuntimeExclusiveTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task BeginExclusive_waits_for_active_operations_then_takes_device_and_clears_cache_in_order()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var op = StartBlockedOperation(runtime, out var entered, out var release);
        await entered.Task.WaitAsync(Wait);

        var exclusive = runtime.BeginExclusiveAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        Assert.Equal(InferenceRuntimeState.Exclusive, runtime.State);
        Assert.False(exclusive.IsCompleted);
        Assert.Empty(engine.Calls); // nothing touches the device while inference is still draining

        release.Set();
        using var lease = await exclusive.WaitAsync(Wait);
        await op.WaitAsync(Wait);

        Assert.Equal(["device-begin", "clear"], engine.Calls);
        Assert.Equal(InferenceRuntimeState.Exclusive, runtime.State);

        lease.Dispose();
        Assert.Equal(["device-begin", "clear", "device-release"], engine.Calls);
        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
    }

    [Fact]
    public async Task Run_is_rejected_during_drain_and_while_exclusive_then_accepted_after_release()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var op = StartBlockedOperation(runtime, out var entered, out var release);
        await entered.Task.WaitAsync(Wait);
        var exclusive = runtime.BeginExclusiveAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        await Assert.ThrowsAsync<InferenceRuntimeUnavailableException>(() =>
            runtime.RunAsync("infer", _ => 1, CancellationToken.None));

        release.Set();
        var lease = await exclusive.WaitAsync(Wait);
        await op.WaitAsync(Wait);
        await Assert.ThrowsAsync<InferenceRuntimeUnavailableException>(() =>
            runtime.RunAsync("infer", _ => 1, CancellationToken.None));

        lease.Dispose();
        Assert.Equal(3, await runtime.RunAsync("infer", _ => 3, CancellationToken.None).WaitAsync(Wait));
    }

    [Fact]
    public async Task BeginExclusive_times_out_when_operations_do_not_drain_and_leaves_runtime_usable()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var op = StartBlockedOperation(runtime, out var entered, out var release);
        await entered.Task.WaitAsync(Wait);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runtime.BeginExclusiveAsync(TimeSpan.FromMilliseconds(30), CancellationToken.None));

        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
        Assert.Empty(engine.Calls);

        release.Set();
        await op.WaitAsync(Wait);
        using var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);
        Assert.Equal(InferenceRuntimeState.Exclusive, runtime.State);
    }

    [Fact]
    public async Task BeginExclusive_cancelled_while_draining_restores_running()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var op = StartBlockedOperation(runtime, out var entered, out var release);
        await entered.Task.WaitAsync(Wait);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.BeginExclusiveAsync(TimeSpan.FromSeconds(30), cts.Token));

        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
        Assert.Empty(engine.Calls);
        release.Set();
        await op.WaitAsync(Wait);
    }

    [Fact]
    public async Task Second_exclusive_is_rejected_without_disturbing_the_first()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        using var first = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.BeginExclusiveAsync(Wait, CancellationToken.None));

        Assert.Equal(InferenceRuntimeState.Exclusive, runtime.State);
        Assert.Equal(["device-begin", "clear"], engine.Calls);
    }

    [Fact]
    public async Task Device_gate_failure_restores_running_and_skips_clear()
    {
        var engine = new FakeEngine { DeviceFailure = new TimeoutException("device busy") };
        var runtime = new InferenceRuntime(engine);

        await Assert.ThrowsAsync<TimeoutException>(() =>
            runtime.BeginExclusiveAsync(Wait, CancellationToken.None));

        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
        Assert.Equal(["device-begin"], engine.Calls);
        Assert.Equal(1, await runtime.RunAsync("infer", _ => 1, CancellationToken.None).WaitAsync(Wait));
    }

    [Fact]
    public async Task Clear_failure_releases_device_and_restores_running()
    {
        var engine = new FakeEngine { ClearFailure = new InvalidOperationException("native") };
        var runtime = new InferenceRuntime(engine);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.BeginExclusiveAsync(Wait, CancellationToken.None));

        Assert.Equal(["device-begin", "clear", "device-release"], engine.Calls);
        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
    }

    [Fact]
    public async Task Exclusive_lease_dispose_is_idempotent()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);

        lease.Dispose();
        lease.Dispose();

        Assert.Equal(1, engine.Calls.Count(call => call == "device-release"));
        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
    }

    [Fact]
    public async Task Shutdown_while_exclusive_stops_runtime_and_late_lease_dispose_does_not_resurrect_it()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);

        await runtime.ShutdownAsync(Wait, CancellationToken.None);
        Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);

        lease.Dispose();

        Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);
        Assert.Contains("device-release", engine.Calls);
    }

    [Fact]
    public async Task Shutdown_during_exclusive_drain_aborts_acquisition_without_touching_the_device()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var op = StartBlockedOperation(runtime, out var entered, out var release);
        await entered.Task.WaitAsync(Wait);
        var exclusive = runtime.BeginExclusiveAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        var shutdown = runtime.ShutdownAsync(Wait, CancellationToken.None);

        release.Set();
        await Assert.ThrowsAsync<InferenceRuntimeUnavailableException>(() => exclusive);
        await shutdown.WaitAsync(Wait);
        await op.WaitAsync(Wait);

        Assert.Equal(InferenceRuntimeState.Stopped, runtime.State);
        Assert.Empty(engine.Calls);
    }

    [Fact]
    public async Task Owner_stop_while_exclusive_completes_and_keeps_exclusive_state()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        using var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);

        await runtime.StopAsync("page", "user", CancellationToken.None).WaitAsync(Wait);

        Assert.Equal(InferenceRuntimeState.Exclusive, runtime.State);
    }

    [Fact]
    public async Task ClearCache_runs_engine_clear_as_a_registered_operation()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);

        await runtime.ClearCacheAsync(CancellationToken.None).WaitAsync(Wait);

        Assert.Equal(["clear"], engine.Calls);
        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task ClearCache_is_rejected_while_exclusive_and_after_shutdown()
    {
        var engine = new FakeEngine();
        var runtime = new InferenceRuntime(engine);
        var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);
        engine.Reset();

        await Assert.ThrowsAsync<InferenceRuntimeUnavailableException>(() =>
            runtime.ClearCacheAsync(CancellationToken.None));

        lease.Dispose();
        await runtime.ShutdownAsync(Wait, CancellationToken.None);
        await Assert.ThrowsAsync<InferenceRuntimeUnavailableException>(() =>
            runtime.ClearCacheAsync(CancellationToken.None));
        Assert.DoesNotContain("clear", engine.Calls);
    }

    [Fact]
    public async Task Exclusive_acquisition_stays_pending_while_the_device_gate_is_held_by_native_inference()
    {
        var deviceFree = new TaskCompletionSource();
        var engine = new FakeEngine { DeviceGate = deviceFree.Task };
        var runtime = new InferenceRuntime(engine);

        var exclusive = runtime.BeginExclusiveAsync(Wait, CancellationToken.None);
        await Task.Delay(30);

        // A request that started through the legacy engine entry still holds the device gate.
        Assert.False(exclusive.IsCompleted);
        Assert.Equal(InferenceRuntimeState.Exclusive, runtime.State);
        Assert.DoesNotContain("clear", engine.Calls);

        deviceFree.SetResult();
        using var lease = await exclusive.WaitAsync(Wait);
        Assert.Equal(["device-begin", "clear"], engine.Calls);
    }

    private static Task<int> StartBlockedOperation(
        InferenceRuntime runtime,
        out TaskCompletionSource entered,
        out ManualResetEventSlim release)
    {
        var enteredSignal = new TaskCompletionSource();
        var releaseSignal = new ManualResetEventSlim();
        entered = enteredSignal;
        release = releaseSignal;
        return runtime.RunAsync("infer", _ =>
        {
            enteredSignal.TrySetResult();
            releaseSignal.Wait(); // simulates native work that ignores cancellation
            return 0;
        }, CancellationToken.None);
    }

    private sealed class FakeEngine : IInferenceEngineControl
    {
        private readonly List<string> _calls = new();

        public IReadOnlyList<string> Calls
        {
            get
            {
                lock (_calls)
                {
                    return _calls.ToArray();
                }
            }
        }

        public Task? DeviceGate { get; init; }

        public Exception? DeviceFailure { get; init; }

        public Exception? ClearFailure { get; init; }

        public void Reset()
        {
            lock (_calls)
            {
                _calls.Clear();
            }
        }

        public async Task<IDisposable> BeginDeviceExclusiveAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            Record("device-begin");
            if (DeviceFailure is not null)
            {
                throw DeviceFailure;
            }

            if (DeviceGate is not null)
            {
                await DeviceGate.WaitAsync(cancellationToken);
            }

            return new Release(this);
        }

        public void ClearCache()
        {
            Record("clear");
            if (ClearFailure is not null)
            {
                throw ClearFailure;
            }
        }

        private void Record(string call)
        {
            lock (_calls)
            {
                _calls.Add(call);
            }
        }

        private sealed class Release(FakeEngine owner) : IDisposable
        {
            private int _disposed;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    owner.Record("device-release");
                }
            }
        }
    }
}
