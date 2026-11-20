using Avalonia.Threading;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// Avalonia <see cref="Dispatcher"/> adapter for the UI-thread port. Same contract as the WPF
/// <c>WpfUiDispatcher</c>: fast paths run inline when already on the UI thread, while the
/// priority overloads always queue so <see cref="UiDispatchPriority.ApplicationIdle"/> still
/// waits for pending layout/binding work.
/// </summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = Dispatcher.UIThread;

    public bool CheckAccess() => _dispatcher.CheckAccess();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Post(action, DispatcherPriority.Normal);
    }

    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        return Queue(action, DispatcherPriority.Normal);
    }

    public Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            return action();
        }

        return Queue(action, DispatcherPriority.Normal);
    }

    public Task InvokeAsync(Action action, UiDispatchPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Queue(action, ToDispatcherPriority(priority));
    }

    public Task InvokeAsync(Func<Task> action, UiDispatchPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        return Queue(action, ToDispatcherPriority(priority));
    }

    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }

    private Task Queue(Action action, DispatcherPriority priority)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(
            () =>
            {
                try
                {
                    action();
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            },
            priority);
        return completion.Task;
    }

    private Task Queue(Func<Task> action, DispatcherPriority priority)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _dispatcher.Post(
            async () =>
            {
                try
                {
                    await action().ConfigureAwait(true);
                    completion.SetResult();
                }
                catch (Exception ex)
                {
                    completion.SetException(ex);
                }
            },
            priority);
        return completion.Task;
    }

    private static DispatcherPriority ToDispatcherPriority(UiDispatchPriority priority) => priority switch
    {
        UiDispatchPriority.Background => DispatcherPriority.Background,
        UiDispatchPriority.ApplicationIdle => DispatcherPriority.ApplicationIdle,
        _ => DispatcherPriority.Normal
    };
}
