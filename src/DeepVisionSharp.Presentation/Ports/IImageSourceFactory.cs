namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// Decodes files/bytes into the host shell's image object. ViewModels must not construct
/// <c>BitmapImage</c> / Avalonia <c>Bitmap</c> directly, and must not cast the returned value:
/// the shell's bindings consume it as-is.
/// </summary>
public interface IImageSourceFactory
{
    object? FromFile(string imagePath);

    /// <summary>
    /// Decode a downscaled thumbnail (decode width in pixels). Use for list/report previews.
    /// </summary>
    object? FromFileThumbnail(string imagePath, int decodePixelWidth);

    object? FromBytes(byte[]? bytes);

    /// <summary>
    /// Same as <see cref="FromFile"/> but also returns pixel size for layout.
    /// </summary>
    bool TryLoadFromFile(string imagePath, out object? image, out int pixelWidth, out int pixelHeight);
}
