using System.IO;
using Avalonia.Media.Imaging;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// Avalonia <see cref="Bitmap"/> 解码适配：ViewModel 只看到 <c>object</c>，绑定层直接把它当图片用。
/// 解码语义与 WPF 版一致：缩略图按解码宽度降采样，像素尺寸从解码后的位图读出。
/// </summary>
public sealed class AvaloniaImageSourceFactory : IImageSourceFactory
{
    public object? FromFile(string imagePath)
        => TryLoadFromFile(imagePath, out var image, out _, out _) ? image : null;

    public object? FromFileThumbnail(string imagePath, int decodePixelWidth)
    {
        if (string.IsNullOrWhiteSpace(imagePath)
            || decodePixelWidth < 1
            || !File.Exists(imagePath))
        {
            return null;
        }

        try
        {
            using var stream = OpenRead(imagePath);
            return Bitmap.DecodeToWidth(stream, decodePixelWidth);
        }
        catch
        {
            return null;
        }
    }

    public object? FromBytes(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        try
        {
            return new Bitmap(new MemoryStream(bytes, writable: false));
        }
        catch
        {
            return null;
        }
    }

    public bool TryLoadFromFile(
        string imagePath,
        out object? image,
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
            using var stream = OpenRead(imagePath);
            var bitmap = new Bitmap(stream);
            image = bitmap;
            pixelWidth = bitmap.PixelSize.Width;
            pixelHeight = bitmap.PixelSize.Height;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>与 WPF 版同一份共享语义：别的进程正在写这张图时也能读。</summary>
    private static FileStream OpenRead(string imagePath) => new(
        imagePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete);
}
