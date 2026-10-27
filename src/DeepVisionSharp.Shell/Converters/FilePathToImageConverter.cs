using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace DeepVisionSharp.Shell.Converters;

/// <summary>
/// Loads an image from a file path by copying bytes into memory.
/// Avoids WPF URI image cache so files overwritten in-place (e.g. training previews)
/// can refresh. Do not use IgnoreImageCache with StreamSource-only — that throws
/// ArgumentNullException (key) in BitmapImage.FinalizeCreation.
/// Caches frozen bitmaps by path + mtime so epoch preview refreshes do not retain
/// unbounded BitmapImage instances when the ItemsControl rebinds the same slots.
/// </summary>
public sealed class FilePathToImageConverter : IValueConverter
{
    private const int MaxCachedImages = 12;

    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, CachedImage> Cache =
        new(StringComparer.OrdinalIgnoreCase);
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
            var cacheKey = path;

            lock (CacheLock)
            {
                if (Cache.TryGetValue(cacheKey, out var cached)
                    && cached.WriteTimeUtc == writeTime)
                {
                    TouchLru_NoLock(cacheKey);
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
                Cache[cacheKey] = new CachedImage(writeTime, image);
                TouchLru_NoLock(cacheKey);
                TrimCache_NoLock();
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
            // Corrupt / incomplete image while another process is rewriting the file.
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static BitmapImage? LoadBitmap(string path)
    {
        using var fileStream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (fileStream.Length <= 0)
        {
            return null;
        }

        using var memory = new MemoryStream(capacity: (int)Math.Min(fileStream.Length, int.MaxValue));
        fileStream.CopyTo(memory);
        if (memory.Length <= 0)
        {
            return null;
        }

        memory.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        // OnLoad copies pixels into the BitmapImage so the stream can be closed.
        // StreamSource (no UriSource) does not hit the URI cache; do not set
        // CreateOptions.IgnoreImageCache here — it requires a non-null UriSource key.
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static void TouchLru_NoLock(string cacheKey)
    {
        LruOrder.Remove(cacheKey);
        LruOrder.AddLast(cacheKey);
    }

    private static void TrimCache_NoLock()
    {
        while (LruOrder.Count > MaxCachedImages)
        {
            var oldest = LruOrder.First;
            if (oldest is null)
            {
                break;
            }

            LruOrder.RemoveFirst();
            Cache.Remove(oldest.Value);
        }
    }

    private sealed record CachedImage(DateTime WriteTimeUtc, BitmapImage Image);
}
