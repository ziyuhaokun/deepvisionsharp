using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceRunnerRuntimeTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task RunSingle_registers_the_backend_call_in_the_runtime()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var backend = new FakeBackend
        {
            Run = (_, _) =>
            {
                entered.SetResult();
                release.Wait();
                return Task.FromResult(new InferenceResult { Success = true });
            }
        };
        var runner = NewRunner(runtime, backend);

        var run = runner.RunSingleAsync("m.torchsharp", "i.png", new InferenceConfig(), "resnet18", CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        Assert.Equal(["inference"], runtime.ActiveOperations);

        release.Set();
        Assert.True((await run.WaitAsync(Wait)).Success);
        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task Warmup_registers_the_backend_call_in_the_runtime()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var backend = new FakeBackend
        {
            Warmup = (_, _) =>
            {
                entered.SetResult();
                release.Wait();
                return Task.FromResult(InferenceWarmupResult.Ok(0, 0, preflightCached: false, loadSkipped: false));
            }
        };
        var runner = NewRunner(runtime, backend);

        var warmup = runner.WarmupAsync("m.torchsharp", new InferenceConfig(), "resnet18", CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        Assert.Equal(["warmup"], runtime.ActiveOperations);

        release.Set();
        Assert.True((await warmup.WaitAsync(Wait)).Success);
        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task RunSingle_returns_failure_instead_of_throwing_while_training_holds_the_runtime()
    {
        var runtime = new InferenceRuntime();
        using var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);
        var backend = new FakeBackend();
        var runner = NewRunner(runtime, backend);

        var result = await runner.RunSingleAsync("m.torchsharp", "i.png", new InferenceConfig(), "resnet18", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("训练", result.Error);
        Assert.Equal(0, backend.RunCalls);
    }

    [Fact]
    public async Task Warmup_returns_failure_instead_of_throwing_while_training_holds_the_runtime()
    {
        var runtime = new InferenceRuntime();
        using var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);
        var backend = new FakeBackend();
        var runner = NewRunner(runtime, backend);

        var result = await runner.WarmupAsync("m.torchsharp", new InferenceConfig(), "resnet18", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(0, backend.WarmupCalls);
    }

    [Fact]
    public async Task Runtime_shutdown_cancellation_surfaces_as_failed_result_not_exception()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        var backend = new FakeBackend
        {
            Run = async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token); // cooperative: ends with OperationCanceledException
                return new InferenceResult { Success = true };
            }
        };
        var runner = NewRunner(runtime, backend);
        var run = runner.RunSingleAsync("m.torchsharp", "i.png", new InferenceConfig(), "resnet18", CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        await runtime.ShutdownAsync(Wait, CancellationToken.None);
        var result = await run.WaitAsync(Wait);

        Assert.False(result.Success);
        Assert.Contains("停止", result.Error);
    }

    [Fact]
    public async Task Caller_cancellation_still_propagates_as_cancellation()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        var backend = new FakeBackend
        {
            Run = async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return new InferenceResult { Success = true };
            }
        };
        var runner = NewRunner(runtime, backend);
        using var cts = new CancellationTokenSource();
        var run = runner.RunSingleAsync("m.torchsharp", "i.png", new InferenceConfig(), "resnet18", cts.Token);
        await entered.Task.WaitAsync(Wait);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task Config_owner_key_is_recorded_on_the_registered_operation()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var backend = new FakeBackend
        {
            Run = (_, _) =>
            {
                entered.SetResult();
                release.Wait();
                return Task.FromResult(new InferenceResult { Success = true });
            }
        };
        var runner = NewRunner(runtime, backend);

        var run = runner.RunSingleAsync(
            "m.torchsharp", "i.png", new InferenceConfig { OwnerKey = "inference-page" }, "resnet18", CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        Assert.Equal(
            new InferenceOperationInfo("inference", "inference-page", IsCancelling: false),
            Assert.Single(runtime.OperationSnapshots));

        release.Set();
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task Runner_stop_cancels_only_the_owners_run_and_reports_it_as_stopped()
    {
        var runtime = new InferenceRuntime();
        var enteredA = new TaskCompletionSource();
        var enteredB = new TaskCompletionSource();
        var backend = new FakeBackend
        {
            Run = async (context, token) =>
            {
                (context.Config.OwnerKey == "a" ? enteredA : enteredB).SetResult();
                await Task.Delay(context.Config.OwnerKey == "a" ? Timeout.Infinite : 300, token);
                return new InferenceResult { Success = true };
            }
        };
        var runner = NewRunner(runtime, backend);
        var runA = runner.RunSingleAsync("m.torchsharp", "i.png", new InferenceConfig { OwnerKey = "a" }, "resnet18", CancellationToken.None);
        var runB = runner.RunSingleAsync("m.torchsharp", "i.png", new InferenceConfig { OwnerKey = "b" }, "resnet18", CancellationToken.None);
        await Task.WhenAll(enteredA.Task, enteredB.Task).WaitAsync(Wait);

        await runner.StopAsync("a", "user", CancellationToken.None).WaitAsync(Wait);

        var resultA = await runA.WaitAsync(Wait);
        Assert.False(resultA.Success);
        Assert.Contains("停止", resultA.Error);
        Assert.True((await runB.WaitAsync(Wait)).Success); // "b" ran to completion untouched
    }

    [Fact]
    public async Task Runner_stop_without_an_owner_changes_nothing()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        var backend = new FakeBackend
        {
            Run = async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(200, token);
                return new InferenceResult { Success = true };
            }
        };
        var runner = NewRunner(runtime, backend);
        var run = runner.RunSingleAsync("m.torchsharp", "i.png", new InferenceConfig { OwnerKey = "a" }, "resnet18", CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        await runner.StopAsync(null, "user", CancellationToken.None).WaitAsync(Wait);

        Assert.True((await run.WaitAsync(Wait)).Success);
    }

    private static InferenceRunner NewRunner(IInferenceRuntime runtime, FakeBackend backend)
        => new(
            new InferenceBackendRegistry([backend]),
            new FakeCapabilities(),
            models: null!,
            projects: null!,
            runtime);

    private sealed class FakeCapabilities : ITaskCapabilityResolver
    {
        public Result ValidateExecution(string? projectType) => Result.Success();

        public Result ValidateModelExecution(string? modelType) => Result.Success();

        public Result<ProjectCapabilityProfile> ForProject(string? projectType) => throw new NotSupportedException();

        public ProjectCapabilityProfile ForProject(ProjectWorkflowKind workflow) => throw new NotSupportedException();

        public Result<ResolvedTaskProfile> ForProjectAndModel(string? projectType, string? modelType)
            => throw new NotSupportedException();
    }

    private sealed class FakeBackend : IInferenceBackend
    {
        private int _runCalls;
        private int _warmupCalls;

        public Func<InferenceBackendContext, CancellationToken, Task<InferenceResult>> Run { get; init; }
            = (_, _) => Task.FromResult(new InferenceResult { Success = true });

        public Func<InferenceBackendContext, CancellationToken, Task<InferenceWarmupResult>> Warmup { get; init; }
            = (_, _) => Task.FromResult(InferenceWarmupResult.Ok(0, 0, preflightCached: true, loadSkipped: true));

        public int RunCalls => Volatile.Read(ref _runCalls);

        public int WarmupCalls => Volatile.Read(ref _warmupCalls);

        public string BackendKey => BackendKeys.InferenceTorchSharpClassification;


        public bool CanHandle(InferenceBackendContext context) => true;

        public Task<InferenceResult> RunSingleAsync(InferenceBackendContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _runCalls);
            return Run(context, cancellationToken);
        }

        Task<InferenceWarmupResult> IInferenceBackend.WarmupAsync(
            InferenceBackendContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _warmupCalls);
            return Warmup(context, cancellationToken);
        }

    }
}
