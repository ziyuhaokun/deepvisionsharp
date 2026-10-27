namespace DeepVisionSharp.TorchSharp.Models;

/// <summary>
/// Bounded least-recently-used cache for native model slots.
/// </summary>
/// <remarks>
/// The inference engine keeps loaded models alive because loading is expensive, but the previous
/// unbounded dictionary never released them: every saved model gets a timestamped file name, so a
/// retraining session created one permanently resident slot per save. Entries are evicted (and
/// disposed) only when they are not leased; an in-use entry is marked pending and disposed when its
/// last lease is released.
/// </remarks>
internal sealed class ModelSlotCache<TKey, TValue>
    where TKey : notnull
    where TValue : class, IDisposable
{
    private readonly int _capacity;
    private readonly object _sync = new();
    private readonly Dictionary<TKey, Entry> _entries = new();
    private readonly LinkedList<TKey> _order = new();

    /// <summary>
    /// Per-key construction locks. Building a model loads native weights and can take seconds; doing
    /// it outside the cache lock keeps other keys usable, while one lock per key stops two callers
    /// from building the same model twice (which doubles peak GPU memory for that model).
    /// </summary>
    private readonly Dictionary<TKey, SemaphoreSlim> _buildLocks = new();

    public ModelSlotCache(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "缓存容量必须大于 0。");
        }

        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    public bool Contains(TKey key)
    {
        lock (_sync)
        {
            return _entries.ContainsKey(key);
        }
    }

    /// <summary>Gets or creates an entry without leasing it (warmup).</summary>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> factory)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                TouchNoLock(cached);
                return cached.Value;
            }
        }

        var created = factory(key);
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var raced))
            {
                created.Dispose();
                TouchNoLock(raced);
                return raced.Value;
            }

            var entry = new Entry(created, _order.AddLast(key));
            _entries[key] = entry;
            EvictNoLock(skip: candidate => EqualityComparer<TKey>.Default.Equals(candidate, key));
            return created;
        }
    }

    /// <summary>
    /// Gets or creates an entry and keeps it alive until the returned lease is disposed.
    /// </summary>
    public Lease Acquire(TKey key, Func<TKey, TValue> factory)
    {
        SemaphoreSlim buildLock;
        lock (_sync)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                cached.InUse++;
                TouchNoLock(cached);
                return new Lease(this, key, cached.Value);
            }

            if (!_buildLocks.TryGetValue(key, out buildLock!))
            {
                _buildLocks[key] = buildLock = new SemaphoreSlim(1, 1);
            }
        }

        buildLock.Wait();
        try
        {
            lock (_sync)
            {
                if (_entries.TryGetValue(key, out var builtWhileWaiting))
                {
                    builtWhileWaiting.InUse++;
                    TouchNoLock(builtWhileWaiting);
                    return new Lease(this, key, builtWhileWaiting.Value);
                }
            }

            var created = factory(key);
            lock (_sync)
            {
                var entry = new Entry(created, _order.AddLast(key)) { InUse = 1 };
                _entries[key] = entry;
                EvictNoLock(skip: candidate => EqualityComparer<TKey>.Default.Equals(candidate, key));
                return new Lease(this, key, created);
            }
        }
        finally
        {
            // The lock object is not disposed here: a waiter may already hold the reference, so
            // disposing it would race them into ObjectDisposedException. Clear() drops the map
            // wholesale once the cache is emptied.
            buildLock.Release();
        }
    }

    /// <summary>Evicts everything that is not in use; in-use entries are released on lease end.</summary>
    public void Clear()
    {
        var immediate = new List<TValue>();
        lock (_sync)
        {
            foreach (var pair in _entries.ToArray())
            {
                if (pair.Value.InUse > 0)
                {
                    pair.Value.PendingEvict = true;
                    continue;
                }

                immediate.Add(pair.Value.Value);
                _entries.Remove(pair.Key);
                _order.Remove(pair.Value.Node);
            }
        }

        DisposeAll(immediate);
    }

    private void Release(TKey key)
    {
        TValue? pending = null;
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry))
            {
                return;
            }

            if (entry.InUse > 0)
            {
                entry.InUse--;
            }

            if (entry.PendingEvict && entry.InUse == 0)
            {
                pending = entry.Value;
                _entries.Remove(key);
                _order.Remove(entry.Node);
            }
            else
            {
                // The released entry may let the cache return to its bound after a transient overflow.
                EvictNoLock();
            }
        }

        if (pending is not null)
        {
            DisposeAll([pending]);
        }
    }

    private void TouchNoLock(Entry entry)
    {
        _order.Remove(entry.Node);
        _order.AddLast(entry.Node);
    }

    private void EvictNoLock(Func<TKey, bool>? skip = null)
    {
        while (_entries.Count > _capacity)
        {
            var node = _order.First;
            var evicted = false;
            while (node is not null)
            {
                var next = node.Next;
                var key = node.Value;
                // Never evict the entry that was just inserted: the caller holds the returned value.
                if ((skip is null || !skip(key))
                    && _entries.TryGetValue(key, out var entry)
                    && entry.InUse == 0)
                {
                    _entries.Remove(key);
                    _order.Remove(node);
                    entry.Value.Dispose();
                    evicted = true;
                    break;
                }

                node = next;
            }

            if (!evicted)
            {
                // Every candidate is leased; allow a temporary overflow and retry after release.
                return;
            }
        }
    }

    private static void DisposeAll(IEnumerable<TValue> values)
    {
        foreach (var value in values)
        {
            try
            {
                value.Dispose();
            }
            catch
            {
                // A native dispose failure must not break the cache or the caller's inference path.
            }
        }
    }

    private sealed class Entry(TValue value, LinkedListNode<TKey> node)
    {
        public TValue Value { get; } = value;
        public LinkedListNode<TKey> Node { get; } = node;
        public int InUse { get; set; }
        public bool PendingEvict { get; set; }
    }

    /// <summary>Keeps an entry from being evicted while native inference is running.</summary>
    public readonly struct Lease : IDisposable
    {
        private readonly ModelSlotCache<TKey, TValue>? _owner;
        private readonly TKey _key;

        internal Lease(ModelSlotCache<TKey, TValue> owner, TKey key, TValue value)
        {
            _owner = owner;
            _key = key;
            Value = value;
        }

        public TValue Value { get; }

        public void Dispose() => _owner?.Release(_key);
    }
}
