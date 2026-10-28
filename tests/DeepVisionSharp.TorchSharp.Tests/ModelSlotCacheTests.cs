using DeepVisionSharp.TorchSharp.Models;
using Xunit;

namespace DeepVisionSharp.TorchSharp.Tests;

public sealed class ModelSlotCacheTests
{
    [Fact]
    public async Task Acquire_and_warmup_share_one_build_per_key()
    {
        using var firstFactoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        var created = 0;
        var cache = new ModelSlotCache<string, TestValue>(capacity: 2);

        var acquireTask = Task.Run(() => cache.Acquire("model", _ =>
        {
            Interlocked.Increment(ref created);
            firstFactoryEntered.Set();
            releaseFactory.Wait();
            return new TestValue();
        }));

        Assert.True(firstFactoryEntered.Wait(TimeSpan.FromSeconds(1)));
        var warmupTask = Task.Run(() => cache.GetOrAdd("model", _ =>
        {
            Interlocked.Increment(ref created);
            return new TestValue();
        }));

        releaseFactory.Set();
        using var lease = await acquireTask;
        var warmed = await warmupTask;

        Assert.Equal(1, created);
        Assert.Same(lease.Value, warmed);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task Clear_during_warmup_disposes_stale_slot_and_prevents_late_insert()
    {
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        var values = new List<TestValue>();
        var factoryCalls = 0;
        var cache = new ModelSlotCache<string, TestValue>(capacity: 2);

        var warmupTask = Task.Run(() => cache.GetOrAdd("model", _ =>
        {
            var value = new TestValue();
            lock (values)
            {
                values.Add(value);
            }

            if (Interlocked.Increment(ref factoryCalls) == 1)
            {
                factoryEntered.Set();
                releaseFactory.Wait();
            }

            return value;
        }));

        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(1)));
        cache.Clear();
        releaseFactory.Set();

        var result = await warmupTask;
        TestValue stale;
        TestValue current;
        lock (values)
        {
            Assert.Equal(2, values.Count);
            stale = values[0];
            current = values[1];
        }

        Assert.Same(current, result);
        Assert.Equal(1, stale.DisposeCount);
        Assert.Equal(0, current.DisposeCount);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task Many_concurrent_callers_on_one_key_build_exactly_once()
    {
        var created = 0;
        var cache = new ModelSlotCache<string, TestValue>(capacity: 2);
        using var start = new ManualResetEventSlim();

        var tasks = Enumerable.Range(0, 16).Select(i => Task.Run(() =>
        {
            start.Wait();
            Func<string, TestValue> factory = _ =>
            {
                Interlocked.Increment(ref created);
                Thread.Sleep(20);
                return new TestValue();
            };

            if (i % 2 == 0)
            {
                using var lease = cache.Acquire("model", factory);
                return lease.Value;
            }

            return cache.GetOrAdd("model", factory);
        })).ToArray();

        start.Set();
        var values = await Task.WhenAll(tasks);

        Assert.Equal(1, created);
        Assert.All(values, v => Assert.Same(values[0], v));
        Assert.Equal(1, cache.Count);
        Assert.Equal(0, values[0].DisposeCount);
    }

    [Fact]
    public async Task Acquire_after_clear_during_build_throws_and_disposes_stale_slot()
    {
        using var factoryEntered = new ManualResetEventSlim();
        using var releaseFactory = new ManualResetEventSlim();
        var stale = new TestValue();
        var cache = new ModelSlotCache<string, TestValue>(capacity: 2);

        var acquire = Task.Run(() => cache.Acquire("model", _ =>
        {
            factoryEntered.Set();
            releaseFactory.Wait();
            return stale;
        }));

        Assert.True(factoryEntered.Wait(TimeSpan.FromSeconds(1)));
        cache.Clear();
        releaseFactory.Set();

        await Assert.ThrowsAsync<InvalidOperationException>(() => acquire);
        Assert.Equal(1, stale.DisposeCount);
        Assert.Equal(0, cache.Count);

        // The failed attempt must not wedge the build lock: a retry succeeds.
        using var retry = cache.Acquire("model", _ => new TestValue());
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void Clear_while_leased_defers_dispose_until_lease_ends()
    {
        var cache = new ModelSlotCache<string, TestValue>(capacity: 2);
        var lease = cache.Acquire("model", _ => new TestValue());
        var value = lease.Value;

        cache.Clear();
        Assert.Equal(0, value.DisposeCount);

        lease.Dispose();
        Assert.Equal(1, value.DisposeCount);
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public async Task Repeated_clear_interleaved_with_warmup_never_leaks_or_double_disposes()
    {
        var all = new List<TestValue>();
        var cache = new ModelSlotCache<string, TestValue>(capacity: 2);
        using var stop = new CancellationTokenSource();

        var clearer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                cache.Clear();
                Thread.Yield();
            }
        });

        var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                var value = cache.GetOrAdd("model", _ =>
                {
                    var v = new TestValue();
                    lock (all)
                    {
                        all.Add(v);
                    }

                    Thread.Sleep(1);
                    return v;
                });
                Assert.NotNull(value);
            }
        })).ToArray();

        await Task.WhenAll(workers);
        stop.Cancel();
        await clearer;
        cache.Clear();

        lock (all)
        {
            // After the final Clear every created slot has been disposed exactly once.
            Assert.All(all, v => Assert.Equal(1, v.DisposeCount));
        }

        Assert.Equal(0, cache.Count);
    }

    private sealed class TestValue : IDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }
}
