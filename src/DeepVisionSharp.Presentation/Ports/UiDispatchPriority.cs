namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// Framework-neutral dispatch priority. The host shell maps each value onto its own
/// dispatcher priority (WPF <c>DispatcherPriority</c>, Avalonia <c>DispatcherPriority</c>).
/// </summary>
public enum UiDispatchPriority
{
    /// <summary>Ordinary work, may run before pending layout.</summary>
    Normal,

    /// <summary>Runs after input and layout: use for background refreshes.</summary>
    Background,

    /// <summary>Runs only when the UI thread is idle: use for work that must observe final layout.</summary>
    ApplicationIdle
}
