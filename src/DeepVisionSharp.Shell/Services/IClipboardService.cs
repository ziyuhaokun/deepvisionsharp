namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Clipboard port. ViewModels use this instead of <c>System.Windows.Clipboard</c>.
/// </summary>
public interface IClipboardService
{
    void SetText(string text);

    /// <summary>Load an image file and place a bitmap on the clipboard.</summary>
    void SetImageFromFile(string imagePath);
}
