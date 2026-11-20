using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Presentation.Services;

public sealed class TrainingPreviewPoller : IDisposable
{
    private readonly TrainingPreviewService _previewService = new();
    private readonly ILogger<TrainingPreviewPoller> _logger;
    private readonly ITrainingProcessHost? _processHost;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private Action<Exception>? _onError;
    private Action<Guid, string>? _previewUpdatedHandler;
    private bool _errorReported;
    private DateTime _lastPreviewCheck = DateTime.MinValue;

    public TrainingPreviewPoller(ILogger<TrainingPreviewPoller> logger, ITrainingProcessHost? processHost = null)
    {
        _logger = logger;
        _processHost = processHost;
    }

    public void Start(
        Guid jobId,
        Func<bool> canContinue,
        Func<Task> onChanged,
        Action<Exception> onError)
    {
        Stop();
        _onError = onError;
        _errorReported = false;
        if (_processHost is not null)
        {
            _previewUpdatedHandler = (updatedJobId, previewPath) =>
            {
                if (updatedJobId == jobId && canContinue())
                {
                    _ = RefreshFromEventAsync(onChanged);
                }
            };
            _processHost.PreviewUpdated += _previewUpdatedHandler;
        }

        _cts = new CancellationTokenSource();
        _runningTask = RunObservedAsync(canContinue, onChanged, _cts.Token);
    }

    public void Stop()
    {
        if (_processHost is not null && _previewUpdatedHandler is not null)
        {
            _processHost.PreviewUpdated -= _previewUpdatedHandler;
            _previewUpdatedHandler = null;
        }

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

    private async Task RefreshFromEventAsync(Func<Task> onChanged)
    {
        try
        {
            await onChanged().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Push-based preview refresh failed");
        }
    }

    public void Dispose() => Stop();

    public void Reset() => _lastPreviewCheck = DateTime.MinValue;

    public IReadOnlyList<TrainingPreviewItem>? GetUpdatedPreviewImages(
        string? outputRoot,
        IReadOnlyList<TrainingPreviewItem> displayedImages,
        bool force)
    {
        var images = _previewService.GetPreviewImages(outputRoot);
        // Legacy files can keep their name when a training epoch overwrites them.
        var lastWrite = images.Count == 0 ? DateTime.MinValue : images.Max(image => File.GetLastWriteTimeUtc(image.ImagePath));
        if (!force && lastWrite <= _lastPreviewCheck && images.SequenceEqual(displayedImages))
        {
            return null;
        }

        _lastPreviewCheck = lastWrite;
        return images;
    }

    private async Task RunObservedAsync(
        Func<bool> canContinue,
        Func<Task> onChanged,
        CancellationToken token)
    {
        try
        {
            await RunAsync(canContinue, onChanged, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Training preview polling stopped unexpectedly.");
            ReportError(ex);
        }
    }

    private static async Task RunAsync(Func<bool> canContinue, Func<Task> onChanged, CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            if (!canContinue())
            {
                continue;
            }

            await onChanged().ConfigureAwait(false);
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

        _logger.LogWarning(task.Exception, "Training preview polling task faulted while stopping.");
    }
}
