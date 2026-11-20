using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace DeepVisionSharp.Shell.Avalonia.Converters.Training;

/// <summary>图片路径 → Avalonia Bitmap：按修改时间缓存最近 12 张，失败返回 null。</summary>
public sealed class FilePathToImageConverter : IValueConverter
{
    private const int MaxCachedImages = 12;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CachedImage> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> LruOrder = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var writeTime = File.GetLastWriteTimeUtc(path);
            lock (CacheLock)
            {
                if (Cache.TryGetValue(path, out var cached) && cached.WriteTimeUtc == writeTime)
                {
                    TouchLruNoLock(path);
                    return cached.Image;
                }
            }

            var image = LoadBitmap(path);
            if (image is null)
            {
                return null;
            }

            lock (CacheLock)
            {
                Cache[path] = new CachedImage(writeTime, image);
                TouchLruNoLock(path);
                TrimCacheNoLock();
            }

            return image;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static Bitmap? LoadBitmap(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    private static void TouchLruNoLock(string key)
    {
        var node = LruOrder.Find(key);
        if (node is not null)
        {
            LruOrder.Remove(node);
        }

        LruOrder.AddFirst(key);
    }

    private static void TrimCacheNoLock()
    {
        while (Cache.Count > MaxCachedImages && LruOrder.Last is { } last)
        {
            LruOrder.RemoveLast();
            // 淘汰时不要 Dispose：被淘汰的 Bitmap 可能仍被 Image 控件（绑定 Source）持有，
            // 提前释放会让控件显示空白/异常。移出缓存后交给 GC，控件释放引用后自然回收。
            Cache.Remove(last.Value);
        }
    }

    private sealed record CachedImage(DateTime WriteTimeUtc, Bitmap? Image);
}