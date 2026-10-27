using OpenCvSharp;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

public sealed class OpenCvSampleThumbnailService : ISampleThumbnailService
{
    private const int MaxCacheEntries = 96;

    private readonly object _sync = new();
    private readonly Dictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();

    public Task<byte[]?> GetThumbnailAsync(
        string imagePath,
        int maxEdgeLength = 200,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => GetThumbnailCore(imagePath, maxEdgeLength, cancellationToken), cancellationToken);
    }

    public void ClearCache()
    {
        lock (_sync)
        {
            _cache.Clear();
            _lru.Clear();
        }
    }

    private byte[]? GetThumbnailCore(string imagePath, int maxEdgeLength, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        var cacheKey = $"{imagePath}|{maxEdgeLength}";
        lock (_sync)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                Touch(cacheKey);
                return cached;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var source = LoadSource(imagePath);
            if (source is null || source.Empty())
            {
                return null;
            }

            using var resized = ResizeToMaxEdge(source, maxEdgeLength);
            if (!Cv2.ImEncode(".jpg", resized, out var bytes) || bytes.Length == 0)
            {
                return null;
            }

            lock (_sync)
            {
                _cache[cacheKey] = bytes;
                Touch(cacheKey);
                while (_lru.Count > MaxCacheEntries)
                {
                    var oldest = _lru.Last!.Value;
                    _lru.RemoveLast();
                    _cache.Remove(oldest);
                }
            }

            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private static Mat? LoadSource(string imagePath)
    {
        var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
        if (!mat.Empty())
        {
            return mat;
        }

        try
        {
            var bytes = File.ReadAllBytes(imagePath);
            var decoded = Cv2.ImDecode(bytes, ImreadModes.Color);
            if (!decoded.Empty())
            {
                mat.Dispose();
                return decoded;
            }
            decoded.Dispose();
        }
        catch
        {
            // Ignore
        }

        return mat;
    }

    private static Mat ResizeToMaxEdge(Mat source, int maxEdgeLength)
    {
        var maxEdge = Math.Max(source.Width, source.Height);
        if (maxEdge <= maxEdgeLength)
        {
            return source.Clone();
        }

        var scale = maxEdgeLength / (double)maxEdge;
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        var resized = new Mat();
        Cv2.Resize(source, resized, new Size(width, height), interpolation: InterpolationFlags.Area);
        return resized;
    }

    private void Touch(string cacheKey)
    {
        _lru.Remove(cacheKey);
        _lru.AddFirst(cacheKey);
    }
}
