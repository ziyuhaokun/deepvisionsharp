using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceStatusReaderTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Reports_running_only_while_an_inference_operation_is_registered()
    {
        var runtime = new InferenceRuntime();
        var reader = new InferenceStatusReader(runtime);
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        Assert.False(reader.IsInferenceRunning);

        var run = runtime.RunAsync("inference", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        Assert.True(reader.IsInferenceRunning);

        release.Set();
        await run.WaitAsync(Wait);
        Assert.False(reader.IsInferenceRunning);
    }

    [Fact]
    public async Task Warmup_and_cache_clear_do_not_count_as_running_inference()
    {
        var runtime = new InferenceRuntime();
        var reader = new InferenceStatusReader(runtime);
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();

        var warmup = runtime.RunAsync("warmup", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None);
        await entered.Task.WaitAsync(Wait);

        Assert.False(reader.IsInferenceRunning);

        release.Set();
        await warmup.WaitAsync(Wait);
    }
}
