namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// Clipboard port. ViewModels use this instead of WPF's <c>Clipboard</c> or
/// Avalonia's <c>IClipboard</c>.
/// </summary>
public interface IClipboardService
{
    void SetText(string text);

    /// <summary>Load an image file and place a bitmap on the clipboard.</summary>
    void SetImageFromFile(string imagePath);
}
