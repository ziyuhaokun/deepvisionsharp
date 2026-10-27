using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// WPF <see cref="BitmapImage"/> decode adapter for ViewModels and Shell services.
/// </summary>
public sealed class WpfImageSourceFactory : IImageSourceFactory
{
    public ImageSource? FromFile(string imagePath)
    {
        return TryLoadFromFile(imagePath, out var image, out _, out _) ? image : null;
    }

    public ImageSource? FromFileThumbnail(string imagePath, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(imagePath)
            || decodePixelWidth < 1
            || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodePixelWidth;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public ImageSource? FromBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public bool TryLoadFromFile(
        string imagePath,
        out ImageSource? image,
        out int pixelWidth,
        out int pixelHeight)
    {
        image = null;
        pixelWidth = 0;
        pixelHeight = 0;

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                imagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            image = bitmap;
            pixelWidth = bitmap.PixelWidth;
            pixelHeight = bitmap.PixelHeight;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
