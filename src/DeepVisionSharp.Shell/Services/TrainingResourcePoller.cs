using Microsoft.Extensions.Logging;

using System.Threading;

namespace DeepVisionSharp.Shell.Services;

public sealed class TrainingResourcePoller : IDisposable
{
    private readonly TrainingResourceMonitor _resourceMonitor;
    private readonly ILogger<TrainingResourcePoller> _logger;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private Action<Exception>? _onError;
    private bool _errorReported;

    public TrainingResourcePoller(
        TrainingResourceMonitor resourceMonitor,
        ILogger<TrainingResourcePoller> logger)
    {
        _resourceMonitor = resourceMonitor;
        _logger = logger;
    }

    public void Start(
        Func<bool> canContinue,
        Action<double, double, double, bool> applyUsage,
        Action<Exception> onError)
    {
        Stop();
        _onError = onError;
        _errorReported = false;
        _cts = new CancellationTokenSource();
        _runningTask = RunObservedAsync(canContinue, applyUsage, _cts.Token);
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        ObserveStoppedTask(_runningTask);
        _runningTask = null;

        try
        {
            _cts?.Dispose();
        }
        catch
        {
        }
        finally
        {
            _cts = null;
            _onError = null;
        }
    }

    public void Dispose() => Stop();

    public async Task UpdateAsync(Action<double, double, double, bool> applyUsage, CancellationToken cancellationToken = default)
    {
        var usage = await _resourceMonitor.GetUsageAsync(cancellationToken).ConfigureAwait(false);
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            applyUsage(usage.CpuPercent, usage.MemoryPercent, usage.GpuUtilization, usage.HasGpu);
        });
    }

    private async Task RunObservedAsync(
        Func<bool> canContinue,
        Action<double, double, double, bool> applyUsage,
        CancellationToken token)
    {
        try
        {
            await RunAsync(canContinue, applyUsage, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Training resource polling stopped unexpectedly.");
            ReportError(ex);
        }
    }

    private async Task RunAsync(
        Func<bool> canContinue,
        Action<double, double, double, bool> applyUsage,
        CancellationToken token)
    {
        await UpdateAsync(applyUsage, token).ConfigureAwait(false);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            if (!canContinue())
            {
                break;
            }

            await UpdateAsync(applyUsage, token).ConfigureAwait(false);
        }
    }

    private void ReportError(Exception ex)
    {
        if (_errorReported)
        {
            return;
        }

        _errorReported = true;
        _onError?.Invoke(ex);
    }

    private void ObserveStoppedTask(Task? task)
    {
        if (task is null || !task.IsFaulted)
        {
            return;
        }

        _logger.LogWarning(task.Exception, "Training resource polling task faulted while stopping.");
    }
}
