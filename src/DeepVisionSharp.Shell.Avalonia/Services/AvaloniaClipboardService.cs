using System.IO;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// Avalonia 剪贴板适配。文本与图片文件写入系统剪贴板；校验与 WPF 版一致
/// （文件不存在 / 空文件直接抛错，调用方据此给出提示）。
/// </summary>
public sealed class AvaloniaClipboardService : IClipboardService
{
    // 系统剪贴板在粘贴时才读 Bitmap，设置后不能立即释放：保留最近一次复制的图片，
    // 等下一次复制成功替换后再释放上一张。
    private Bitmap? _lastCopiedImage;

    public void SetText(string text)
    {
        var clipboard = GetClipboard();
        if (clipboard is null || string.IsNullOrEmpty(text))
        {
            return;
        }

        _ = clipboard.SetTextAsync(text);
    }

    public void SetImageFromFile(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            throw new FileNotFoundException("Image file not found.", imagePath);
        }

        var bitmap = LoadBitmap(imagePath);
        var clipboard = GetClipboard();
        if (clipboard is null)
        {
            bitmap.Dispose();
            throw new InvalidOperationException("剪贴板不可用。");
        }

        var pending = clipboard.SetBitmapAsync(bitmap);
        pending.ContinueWith(
            task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var previous = Interlocked.Exchange(ref _lastCopiedImage, bitmap);
                    previous?.Dispose();
                    return;
                }

                // 未成功交给剪贴板，本次加载的 Bitmap 由这里负责释放。
                bitmap.Dispose();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static Bitmap LoadBitmap(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length <= 0)
        {
            throw new InvalidOperationException("Image file is empty.");
        }

        return new Bitmap(stream);
    }

    private static IClipboard? GetClipboard()
        => TopLevel.GetTopLevel(ShellWindowHost.Window)?.Clipboard;
}
