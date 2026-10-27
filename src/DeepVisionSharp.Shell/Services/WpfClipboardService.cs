using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// WPF clipboard adapter. Keep all Clipboard / BitmapImage clipboard work here.
/// </summary>
public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        System.Windows.Clipboard.SetText(text);
    }

    public void SetImageFromFile(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file not found.", imagePath);
        }

        using var fileStream = new FileStream(
            imagePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        if (fileStream.Length <= 0)
        {
            throw new InvalidOperationException("Image file is empty.");
        }

        using var memory = new MemoryStream(capacity: (int)Math.Min(fileStream.Length, int.MaxValue));
        fileStream.CopyTo(memory);
        if (memory.Length <= 0)
        {
            throw new InvalidOperationException("Image file is empty.");
        }

        memory.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = memory;
        image.EndInit();
        image.Freeze();

        System.Windows.Clipboard.SetImage(image);
    }
}
