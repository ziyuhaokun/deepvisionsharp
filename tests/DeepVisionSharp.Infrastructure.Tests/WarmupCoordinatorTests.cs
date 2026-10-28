using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class WarmupCoordinatorTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Requesting_the_same_target_twice_warms_up_once()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Block };
        var coordinator = new WarmupCoordinator(runner);

        coordinator.Request(Target("m1.torchsharp"));
        await runner.FirstWarmupEntered.WaitAsync(Wait);
        coordinator.Request(Target("m1.torchsharp"));

        runner.Release();
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.Equal(1, runner.WarmupCalls);
    }

    [Fact]
    public async Task A_new_target_waits_for_the_running_one_to_finish_before_starting()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Block };
        var coordinator = new WarmupCoordinator(runner);
        var starts = new List<string>();

        coordinator.Request(Target("a.torchsharp"));
        await runner.FirstWarmupEntered.WaitAsync(Wait);

        coordinator.Request(Target("b.torchsharp"));

        // "a" is still inside its (uninterruptible) native load: "b" must not start yet.
        await Task.Delay(50);
        Assert.Equal(["a.torchsharp"], runner.StartedPaths());

        runner.Release();
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.Equal(["a.torchsharp", "b.torchsharp"], runner.StartedPaths());
        Assert.Equal(1, runner.MaxConcurrentWarmups);
        Assert.True(runner.Tokens[0].IsCancellationRequested); // the stale target was asked to stop
    }

    [Fact]
    public async Task Only_the_first_and_the_latest_target_run_when_several_are_requested()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Block };
        var coordinator = new WarmupCoordinator(runner);

        coordinator.Request(Target("a.torchsharp"));
        await runner.FirstWarmupEntered.WaitAsync(Wait);
        coordinator.Request(Target("b.torchsharp"));
        coordinator.Request(Target("c.torchsharp"));

        runner.Release();
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.Equal(["a.torchsharp", "c.torchsharp"], runner.StartedPaths());
    }

    [Fact]
    public async Task Wait_for_idle_returns_immediately_when_nothing_was_requested()
    {
        var runner = new FakeRunner();
        var coordinator = new WarmupCoordinator(runner);

        Assert.True(await coordinator.WaitForIdleAsync(Wait).WaitAsync(Wait));
        Assert.Equal(0, runner.WarmupCalls);
    }

    [Fact]
    public async Task Wait_for_idle_returns_only_after_the_native_load_returned()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Block };
        var coordinator = new WarmupCoordinator(runner);
        coordinator.Request(Target("a.torchsharp"));
        await runner.FirstWarmupEntered.WaitAsync(Wait);

        var idle = coordinator.WaitForIdleAsync(Wait);
        await Task.Delay(30);
        Assert.False(idle.IsCompleted);

        runner.Release();
        Assert.True(await idle.WaitAsync(Wait));
    }

    [Fact]
    public async Task Wait_for_idle_reports_false_at_timeout_while_native_work_continues()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Block };
        var coordinator = new WarmupCoordinator(runner);
        coordinator.Request(Target("a.torchsharp"));
        await runner.FirstWarmupEntered.WaitAsync(Wait);

        Assert.False(await coordinator.WaitForIdleAsync(TimeSpan.FromMilliseconds(30)));
        Assert.True(coordinator.IsRunning);

        runner.Release();
        Assert.True(await coordinator.WaitForIdleAsync(Wait));
        Assert.False(coordinator.IsRunning);
    }

    [Fact]
    public async Task Cancel_drops_the_queued_target_so_it_never_starts()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Block };
        var coordinator = new WarmupCoordinator(runner);
        coordinator.Request(Target("a.torchsharp"));
        await runner.FirstWarmupEntered.WaitAsync(Wait);
        coordinator.Request(Target("b.torchsharp"));

        coordinator.Cancel();
        runner.Release();
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.Equal(["a.torchsharp"], runner.StartedPaths());
        Assert.Null(coordinator.DesiredKey);
    }

    [Fact]
    public async Task A_failed_warmup_is_reported_and_does_not_wedge_the_coordinator()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Fail };
        var coordinator = new WarmupCoordinator(runner);
        WarmupOutcome? outcome = null;
        coordinator.Completed += result => outcome = result;

        coordinator.Request(Target("bad.torchsharp"));
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.NotNull(outcome);
        Assert.False(outcome!.Success);
        Assert.False(outcome.Cancelled);
        Assert.Equal("native boom", outcome.Error);

        runner.Mode = FakeRunner.WarmupMode.Succeed;
        coordinator.Request(Target("good.torchsharp"));
        Assert.True(await coordinator.WaitForIdleAsync(Wait));
        Assert.Equal(2, runner.WarmupCalls);
    }

    [Fact]
    public async Task A_throwing_warmup_is_caught_and_reported()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Throw };
        var coordinator = new WarmupCoordinator(runner);
        WarmupOutcome? outcome = null;
        coordinator.Completed += result => outcome = result;

        coordinator.Request(Target("bad.torchsharp"));
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.NotNull(outcome);
        Assert.False(outcome!.Success);
        Assert.Equal("kaboom", outcome.Error);
    }

    [Fact]
    public async Task A_cancelled_warmup_reports_cancelled_rather_than_failed()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Cooperative };
        var coordinator = new WarmupCoordinator(runner);
        WarmupOutcome? outcome = null;
        coordinator.Completed += result => outcome = result;
        coordinator.Request(Target("a.torchsharp"));
        await runner.FirstWarmupEntered.WaitAsync(Wait);

        coordinator.Cancel();
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.NotNull(outcome);
        Assert.True(outcome!.Cancelled);
    }

    [Fact]
    public async Task A_target_can_be_requested_again_after_a_cancel()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Succeed };
        var coordinator = new WarmupCoordinator(runner);
        coordinator.Request(Target("a.torchsharp"));
        Assert.True(await coordinator.WaitForIdleAsync(Wait));
        coordinator.Cancel();

        coordinator.Request(Target("a.torchsharp"));
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.Equal(2, runner.WarmupCalls);
    }

    [Fact]
    public async Task Completed_is_raised_outside_the_coordinators_lock()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Succeed };
        var coordinator = new WarmupCoordinator(runner);
        var reentered = false;
        coordinator.Completed += outcome =>
        {
            if (outcome.Request.ModelPath != "a.torchsharp")
            {
                return;
            }

            // Requesting from the callback must not deadlock on the coordinator's own lock.
            coordinator.Request(Target("b.torchsharp"));
            reentered = true;
        };

        coordinator.Request(Target("a.torchsharp"));
        Assert.True(await coordinator.WaitForIdleAsync(Wait));

        Assert.True(reentered);
        Assert.Equal(2, runner.WarmupCalls);
    }

    [Fact]
    public async Task Interleaved_requests_never_run_two_warmups_at_once()
    {
        var runner = new FakeRunner { Mode = FakeRunner.WarmupMode.Untimed };
        var coordinator = new WarmupCoordinator(runner);

        for (var round = 0; round < 200; round++)
        {
            coordinator.Request(Target($"m{round % 5}.torchsharp"));
            if (round % 3 == 0)
            {
                coordinator.Cancel();
            }

            if (round % 7 == 0)
            {
                await coordinator.WaitForIdleAsync(TimeSpan.FromMilliseconds(5));
            }
        }

        coordinator.Request(Target("final.torchsharp"));
        Assert.True(await coordinator.WaitForIdleAsync(Wait));
        Assert.Equal(1, runner.MaxConcurrentWarmups);
    }

    private static WarmupRequest Target(string path) =>
        new(path, "resnet18", new InferenceConfig { Device = "cuda:0", OwnerKey = "inference-page" });

    private sealed class FakeRunner : IInferenceRunner
    {
        public enum WarmupMode
        {
            Succeed,
            Block,
            Cooperative,
            Fail,
            Throw,
            Untimed
        }

        private readonly object _sync = new();
        private readonly List<string> _started = new();
        private readonly List<CancellationToken> _tokens = new();
        private readonly TaskCompletionSource _firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ManualResetEventSlim _release = new();
        private int _concurrent;
        private int _maxConcurrent;
        private int _warmupCalls;

        public WarmupMode Mode { get; set; } = WarmupMode.Succeed;

        public Task FirstWarmupEntered => _firstEntered.Task;

        public int WarmupCalls => Volatile.Read(ref _warmupCalls);

        public int MaxConcurrentWarmups => Volatile.Read(ref _maxConcurrent);

        public List<CancellationToken> Tokens
        {
            get
            {
                lock (_sync)
                {
                    return _tokens.ToList();
                }
            }
        }

        public List<string> StartedPaths()
        {
            lock (_sync)
            {
                return _started.ToList();
            }
        }

        public void Release() => _release.Set();

        public Task<InferenceResult> RunSingleAsync(
            string modelPath,
            string imagePath,
            InferenceConfig config,
            string modelType,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public async Task<InferenceWarmupResult> WarmupAsync(
            string modelPath,
            InferenceConfig config,
            string modelType,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _started.Add(modelPath);
                _tokens.Add(cancellationToken);
            }

            Interlocked.Increment(ref _warmupCalls);
            var concurrent = Interlocked.Increment(ref _concurrent);
            UpdateMax(ref _maxConcurrent, concurrent);
            _firstEntered.TrySetResult();

            try
            {
                switch (Mode)
                {
                    case WarmupMode.Block:
                        // Simulates a native load that ignores the token entirely.
                        _release.Wait(Wait);
                        break;
                    case WarmupMode.Cooperative:
                        await Task.Delay(Timeout.Infinite, cancellationToken);
                        break;
                    case WarmupMode.Untimed:
                        await Task.Delay(2);
                        break;
                    case WarmupMode.Throw:
                        throw new InvalidOperationException("kaboom");
                    case WarmupMode.Fail:
                        return InferenceWarmupResult.Fail("native boom");
                }

                cancellationToken.ThrowIfCancellationRequested();
                return InferenceWarmupResult.Ok(0, 0, preflightCached: false, loadSkipped: false);
            }
            catch (OperationCanceledException)
            {
                if (Mode == WarmupMode.Cooperative)
                {
                    throw;
                }

                throw;
            }
            finally
            {
                Interlocked.Decrement(ref _concurrent);
            }
        }

        public Task StopAsync(string? ownerKey, string? reason, CancellationToken cancellationToken)
            => Task.CompletedTask;

        private static void UpdateMax(ref int max, int value)
        {
            int current;
            while (value > (current = Volatile.Read(ref max)))
            {
                if (Interlocked.CompareExchange(ref max, value, current) == current)
                {
                    return;
                }
            }
        }
    }
}
