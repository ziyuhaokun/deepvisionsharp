using System.Windows.Media;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Decodes files/bytes into frozen WPF <see cref="ImageSource"/> instances.
/// ViewModels must not construct <c>BitmapImage</c> directly.
/// </summary>
public interface IImageSourceFactory
{
    ImageSource? FromFile(string imagePath);

    /// <summary>
    /// Decode a downscaled thumbnail (decode width in pixels). Use for list/report previews.
    /// </summary>
    ImageSource? FromFileThumbnail(string imagePath, int decodePixelWidth);

    ImageSource? FromBytes(byte[]? bytes);

    /// <summary>
    /// Same as <see cref="FromFile"/> but also returns pixel size for layout.
    /// </summary>
    bool TryLoadFromFile(string imagePath, out ImageSource? image, out int pixelWidth, out int pixelHeight);
}
