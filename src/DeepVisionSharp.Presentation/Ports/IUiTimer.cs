namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// Repeating timer whose ticks are delivered on the UI thread, created through
/// <see cref="IUiTimerFactory"/>. ViewModels use this instead of <c>DispatcherTimer</c>.
/// </summary>
public interface IUiTimer : IDisposable
{
    event EventHandler? Tick;

    bool IsEnabled { get; }

    void Start();

    void Stop();
}
