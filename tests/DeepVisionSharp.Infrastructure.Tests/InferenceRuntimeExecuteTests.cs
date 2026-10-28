using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceRuntimeExecuteTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Execute_registers_operation_while_async_work_runs_and_releases_it_afterwards()
    {
        var runtime = new InferenceRuntime();
        var gate = new TaskCompletionSource<int>();

        var run = runtime.ExecuteAsync("warmup", _ => gate.Task, CancellationToken.None);
        await Task.Delay(30);

        Assert.Equal(["warmup"], runtime.ActiveOperations);

        gate.SetResult(9);
        Assert.Equal(9, await run.WaitAsync(Wait));
        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task Execute_releases_registration_when_async_work_faults()
    {
        var runtime = new InferenceRuntime();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtime.ExecuteAsync<int>("warmup", _ => Task.FromException<int>(new InvalidOperationException("boom")), CancellationToken.None));

        Assert.Equal(0, runtime.ActiveOperationCount);
    }

    [Fact]
    public async Task Execute_is_rejected_while_exclusive()
    {
        var runtime = new InferenceRuntime();
        using var lease = await runtime.BeginExclusiveAsync(Wait, CancellationToken.None);

        await Assert.ThrowsAsync<InferenceRuntimeUnavailableException>(() =>
            runtime.ExecuteAsync("warmup", _ => Task.FromResult(1), CancellationToken.None));
    }
}
