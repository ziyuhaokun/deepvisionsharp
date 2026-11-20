
using System.Windows;
using System.Windows.Threading;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// WPF <see cref="Dispatcher"/> adapter for the UI-thread port. Keep all Application.Current usage here.
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    // Capture the owner when composed on the UI thread, also in an STA verification
    // process without Application.Current. Worker continuations must not run UI mutations inline.
    private readonly Dispatcher _owner = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    private Dispatcher? CurrentDispatcher => _owner;

    public bool CheckAccess()
    {
        var dispatcher = CurrentDispatcher;
        return dispatcher is null || dispatcher.CheckAccess();
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = CurrentDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Normal, action);
    }

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = CurrentDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return dispatcher.InvokeAsync(action).Task;
    }

    public Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = CurrentDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return action();
        }

        return dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    public Task InvokeAsync(Action action, UiDispatchPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = CurrentDispatcher;
        if (dispatcher is null)
        {
            action();
            return Task.CompletedTask;
        }

        // Always queue (even when already on the UI thread) so ApplicationIdle / Background
        // semantics hold and the action runs after pending layout/bind operations.
        return dispatcher.InvokeAsync(action, ToDispatcherPriority(priority)).Task;
    }

    public Task InvokeAsync(Func<Task> action, UiDispatchPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = CurrentDispatcher;
        if (dispatcher is null)
        {
            return action();
        }

        return dispatcher.InvokeAsync(action, ToDispatcherPriority(priority)).Task.Unwrap();
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var dispatcher = CurrentDispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Invoke(action);
    }

    private static DispatcherPriority ToDispatcherPriority(UiDispatchPriority priority) => priority switch
    {
        UiDispatchPriority.Background => DispatcherPriority.Background,
        UiDispatchPriority.ApplicationIdle => DispatcherPriority.ApplicationIdle,
        _ => DispatcherPriority.Normal
    };
}
