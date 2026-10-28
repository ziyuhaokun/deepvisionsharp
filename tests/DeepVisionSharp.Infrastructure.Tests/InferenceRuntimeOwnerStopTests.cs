using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceRuntimeOwnerStopTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Stop_cancels_only_the_named_owner()
    {
        var runtime = new InferenceRuntime();
        var a = StartCooperative(runtime, "a");
        var b = StartCooperative(runtime, "b");
        await Task.WhenAll(a.Entered.Task, b.Entered.Task).WaitAsync(Wait);

        await runtime.StopAsync("a", "user", CancellationToken.None).WaitAsync(Wait);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.Run);
        Assert.True(a.Token.IsCancellationRequested);
        Assert.False(b.Token.IsCancellationRequested);
        Assert.Equal(1, runtime.ActiveOperationCount);
        Assert.Equal("b", Assert.Single(runtime.OperationSnapshots).OwnerKey);

        b.Finish.Set();
        Assert.Equal(1, await b.Run.WaitAsync(Wait));
    }

    [Fact]
    public async Task Stop_returns_only_after_that_owners_native_work_has_finished()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var run = runtime.RunAsync("infer", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None, "a");
        await entered.Task.WaitAsync(Wait);

        var stop = runtime.StopAsync("a", "user", CancellationToken.None);
        await Task.Delay(30);
        Assert.False(stop.IsCompleted);

        release.Set();
        await stop.WaitAsync(Wait);
        Assert.Equal(0, runtime.ActiveOperationCount); // the drain guarantee: nothing of "a" is left
        await run.WaitAsync(Wait);
    }

    [Fact]
    public async Task Stop_does_not_wait_for_other_owners_work()
    {
        var runtime = new InferenceRuntime();
        var a = StartCooperative(runtime, "a");
        var b = StartCooperative(runtime, "b");
        await Task.WhenAll(a.Entered.Task, b.Entered.Task).WaitAsync(Wait);

        // "b" stays blocked for the whole test; stopping "a" must still complete.
        await runtime.StopAsync("a", "user", CancellationToken.None).WaitAsync(Wait);

        Assert.True(b.Run.IsCompleted == false);
        b.Finish.Set();
        await b.Run.WaitAsync(Wait);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Stop_with_a_blank_owner_is_a_noop(string? ownerKey)
    {
        var runtime = new InferenceRuntime();
        var a = StartCooperative(runtime, "a");
        await a.Entered.Task.WaitAsync(Wait);

        await runtime.StopAsync(ownerKey, "user", CancellationToken.None).WaitAsync(Wait);

        Assert.False(a.Token.IsCancellationRequested);
        Assert.Equal(1, runtime.ActiveOperationCount);
        a.Finish.Set();
        await a.Run.WaitAsync(Wait);
    }

    [Fact]
    public async Task Stop_for_an_owner_with_no_operations_completes_immediately()
    {
        var runtime = new InferenceRuntime();

        await runtime.StopAsync("nobody", "user", CancellationToken.None).WaitAsync(Wait);

        Assert.Equal(InferenceRuntimeState.Running, runtime.State);
    }

    [Fact]
    public async Task Operations_without_an_owner_are_never_touched_by_an_owner_stop()
    {
        var runtime = new InferenceRuntime();
        var anonymous = StartCooperative(runtime, ownerKey: null);
        await anonymous.Entered.Task.WaitAsync(Wait);

        await runtime.StopAsync("a", "user", CancellationToken.None).WaitAsync(Wait);

        Assert.False(anonymous.Token.IsCancellationRequested);
        anonymous.Finish.Set();
        await anonymous.Run.WaitAsync(Wait);
    }

    [Fact]
    public async Task New_operations_are_accepted_while_an_owner_is_stopping()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var blocked = runtime.RunAsync("infer", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None, "a");
        await entered.Task.WaitAsync(Wait);
        var stop = runtime.StopAsync("a", "user", CancellationToken.None);

        var other = await runtime.RunAsync("infer", _ => 2, CancellationToken.None, "b").WaitAsync(Wait);
        var same = await runtime.RunAsync("infer", _ => 3, CancellationToken.None, "a").WaitAsync(Wait);

        Assert.Equal(2, other);
        Assert.Equal(3, same);
        release.Set();
        await stop.WaitAsync(Wait);
        await blocked.WaitAsync(Wait);
    }

    [Fact]
    public async Task Stop_wait_honours_the_caller_token_but_the_cancelling_flag_persists_until_drained()
    {
        var runtime = new InferenceRuntime();
        var entered = new TaskCompletionSource();
        using var release = new ManualResetEventSlim();
        var run = runtime.RunAsync("infer", _ =>
        {
            entered.SetResult();
            release.Wait();
            return 0;
        }, CancellationToken.None, "a");
        await entered.Task.WaitAsync(Wait);

        using var waitCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.StopAsync("a", "user", waitCts.Token));

        Assert.True(Assert.Single(runtime.OperationSnapshots).IsCancelling);

        release.Set();
        await run.WaitAsync(Wait);
        Assert.Empty(runtime.OperationSnapshots);
    }

    [Fact]
    public async Task Snapshots_expose_kind_owner_and_cancelling_flag_per_operation()
    {
        var runtime = new InferenceRuntime();
        var a = StartCooperative(runtime, "a", kind: "warmup");
        var b = StartCooperative(runtime, "b", kind: "infer");
        await Task.WhenAll(a.Entered.Task, b.Entered.Task).WaitAsync(Wait);

        var snapshots = runtime.OperationSnapshots.OrderBy(info => info.OwnerKey).ToArray();

        Assert.Equal(
            [new InferenceOperationInfo("warmup", "a", false), new InferenceOperationInfo("infer", "b", false)],
            snapshots);
        a.Finish.Set();
        b.Finish.Set();
        await Task.WhenAll(a.Run, b.Run).WaitAsync(Wait);
    }

    [Fact]
    public async Task Stop_racing_with_completion_never_hangs_or_throws()
    {
        for (var round = 0; round < 200; round++)
        {
            var runtime = new InferenceRuntime();
            using var start = new ManualResetEventSlim();
            var run = Task.Run(async () =>
            {
                start.Wait();
                try
                {
                    await runtime.RunAsync("infer", token =>
                    {
                        Thread.Sleep(1);
                        return 0;
                    }, CancellationToken.None, "a");
                }
                catch (OperationCanceledException)
                {
                }
            });
            var stop = Task.Run(async () =>
            {
                start.Wait();
                await runtime.StopAsync("a", "user", CancellationToken.None);
            });

            start.Set();
            await Task.WhenAll(run, stop).WaitAsync(Wait);
            Assert.Equal(0, runtime.ActiveOperationCount);
        }
    }

    private static Cooperative StartCooperative(InferenceRuntime runtime, string? ownerKey, string kind = "infer")
    {
        var entered = new TaskCompletionSource();
        var finish = new ManualResetEventSlim();
        var captured = new CancellationToken[1];
        var run = runtime.RunAsync(kind, token =>
        {
            captured[0] = token;
            entered.SetResult();
            // Wakes on cancellation (cooperative) or on Finish; reports which by throwing.
            WaitHandle.WaitAny([token.WaitHandle, finish.WaitHandle], Wait);
            token.ThrowIfCancellationRequested();
            return 1;
        }, CancellationToken.None, ownerKey);
        return new Cooperative(run, entered, finish, captured);
    }

    private sealed class Cooperative(
        Task<int> run,
        TaskCompletionSource entered,
        ManualResetEventSlim finish,
        CancellationToken[] token)
    {
        public Task<int> Run => run;

        public TaskCompletionSource Entered => entered;

        public ManualResetEventSlim Finish => finish;

        public CancellationToken Token => token[0];
    }
}
