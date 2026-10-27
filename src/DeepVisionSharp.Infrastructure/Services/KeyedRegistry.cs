using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Keyed registry shared by backend / artifact-handler sets.
/// Blank or duplicate keys fail at construction (DI startup), so resolve never sees an ambiguous map.
/// </summary>
internal abstract class KeyedRegistry<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly IReadOnlyDictionary<string, T> _byKey;

    protected KeyedRegistry(IEnumerable<T> items, Func<T, string> keySelector, string registryName)
    {
        _items = items.ToArray();

        var map = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in _items)
        {
            var key = keySelector(item)?.Trim() ?? string.Empty;
            if (key.Length == 0)
            {
                throw new InvalidOperationException($"{registryName} 注册项缺少键");
            }

            if (!map.TryAdd(key, item))
            {
                throw new InvalidOperationException($"{registryName} 重复注册键：{key}");
            }
        }

        _byKey = map;
    }

    protected IReadOnlyList<T> Items => _items;

    protected abstract string EmptyKeyMessage { get; }

    protected abstract string GetNotRegisteredMessage(string key);

    /// <summary>Authoritative resolve by the stored / catalog key.</summary>
    public Result<T> ResolveByKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Result<T>.Failure(EmptyKeyMessage);
        }

        var trimmed = key.Trim();
        return _byKey.TryGetValue(trimmed, out var item)
            ? Result<T>.Success(item)
            : Result<T>.Failure(GetNotRegisteredMessage(trimmed));
    }
}
