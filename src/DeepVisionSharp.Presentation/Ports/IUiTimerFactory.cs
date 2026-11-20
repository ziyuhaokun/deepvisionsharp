namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// Creates UI-thread timers. Only the host shell knows how to build one
/// (WPF <c>DispatcherTimer</c>, Avalonia <c>DispatcherTimer</c>).
/// </summary>
public interface IUiTimerFactory
{
    IUiTimer Create(TimeSpan interval);
}
