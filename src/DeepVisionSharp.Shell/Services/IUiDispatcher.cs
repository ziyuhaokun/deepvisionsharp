using System.Windows.Threading;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// UI-thread marshaling port. ViewModels use this instead of
/// <c>System.Windows.Application.Current.Dispatcher</c>.
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
    /// the UI thread, so <see cref="DispatcherPriority.ApplicationIdle"/> waits for layout).
    /// </summary>
    Task InvokeAsync(Action action, DispatcherPriority priority);

    Task InvokeAsync(Func<Task> action, DispatcherPriority priority);

    /// <summary>Synchronously run on the UI thread; no-op fast path when already on it.</summary>
    void Invoke(Action action);
}
