using System.Windows.Threading;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Disposes a page's view model once the page is really gone.
/// </summary>
/// <remarks>
/// Pages and their view models are registered Transient, and most view models implement
/// <see cref="IDisposable"/>. The DI root tracks every disposable it creates, so a page visited
/// repeatedly keeps one view-model graph per visit alive until the process exits — verified against
/// Microsoft.Extensions.DependencyInjection.
/// <para>
/// Disposing synchronously from <c>Unloaded</c> is not an option: NavigationView re-raises
/// <c>Loaded</c> on the same page instance, and tearing the view model down mid-cycle races in-flight
/// loads and DispatcherTimer teardown (documented crash in TrainingPage). Instead, unload schedules a
/// dispose and load cancels it; if the page is not re-shown within the delay, it is discarded and the
/// view model is disposed.
/// </para>
/// </remarks>
public sealed class PageDisposalScheduler
{
    /// <summary>
    /// Grace period before an unloaded page is treated as discarded. Long enough to cover a
    /// NavigationView unload/reload cycle, short enough that a user moving between pages does not
    /// accumulate several graphs.
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(5);

    private readonly Func<IDisposable?> _target;
    private readonly Action<Exception>? _onError;
    private readonly DispatcherTimer _timer;
    private int _generation;

    /// <param name="target">Returns the view model to dispose, or null when there is nothing to do.</param>
    /// <param name="onError">Called when disposal throws; disposal must never break navigation.</param>
    public PageDisposalScheduler(Func<IDisposable?> target, Action<Exception>? onError = null)
        : this(target, onError, DefaultDelay)
    {
    }

    public PageDisposalScheduler(Func<IDisposable?> target, Action<Exception>? onError, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
        _onError = onError;
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += OnTick;
    }

    /// <summary>Cancels a pending dispose. Call from the page's Loaded handler.</summary>
    public void CancelPendingDispose()
    {
        _generation++;
        _timer.Stop();
    }

    /// <summary>Schedules a dispose unless <see cref="CancelPendingDispose"/> runs first.</summary>
    public void ScheduleDispose()
    {
        var generation = _generation;
        _timer.Stop();
        _timer.Tag = generation;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        if (_timer.Tag is int scheduled && scheduled != _generation)
        {
            // Re-shown: the pending dispose was cancelled.
            return;
        }

        try
        {
            _target()?.Dispose();
        }
        catch (Exception ex)
        {
            _onError?.Invoke(ex);
        }
    }
}
