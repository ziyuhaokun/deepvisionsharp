namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// UI-thread marshaling port. ViewModels use this instead of
/// <c>Application.Current.Dispatcher</c> or Avalonia's <c>Dispatcher.UIThread</c>.
/// </summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    /// <summary>Run immediately if already on the UI thread; otherwise post asynchronously.</summary>
    void Post(Action action);

    Task InvokeAsync(Action action);

    Task InvokeAsync(Func<Task> action);

    /// <summary>
    /// Invoke on the UI thread at the given priority (always queues, even when already on
    /// the UI thread, so <see cref="UiDispatchPriority.ApplicationIdle"/> waits for layout).
    /// </summary>
    Task InvokeAsync(Action action, UiDispatchPriority priority);

    Task InvokeAsync(Func<Task> action, UiDispatchPriority priority);

    /// <summary>Synchronously run on the UI thread; no-op fast path when already on it.</summary>
    void Invoke(Action action);
}
