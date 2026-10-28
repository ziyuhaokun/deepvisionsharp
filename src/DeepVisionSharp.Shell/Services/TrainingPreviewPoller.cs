using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Shell.Services;

public sealed class TrainingPreviewPoller : IDisposable
{
    private readonly TrainingPreviewService _previewService = new();
    private readonly ILogger<TrainingPreviewPoller> _logger;
    private readonly ITrainingProcessHost? _processHost;
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private Action<Exception>? _onError;
    private Func<Task>? _activeOnChanged;
    private Func<bool>? _activeCanContinue;
    private bool _errorReported;
    private DateTime _lastPreviewCheck = DateTime.MinValue;

    public TrainingPreviewPoller(ILogger<TrainingPreviewPoller> logger, ITrainingProcessHost? processHost = null)
    {
        _logger = logger;
        _processHost = processHost;
    }

    public void Start(
        string? outputRoot,
        ObservableCollection<string> previewImagePaths,
        Func<bool> canContinue,
        Func<Task> onChanged,
        Action<Exception> onError)
    {
        Stop();
        _onError = onError;
        _errorReported = false;
        _activeCanContinue = canContinue;
        _activeOnChanged = onChanged;
        if (_processHost is not null)
        {
            _processHost.PreviewUpdated += OnProcessHostPreviewUpdated;
        }

        _cts = new CancellationTokenSource();
        _runningTask = RunObservedAsync(canContinue, onChanged, _cts.Token);
    }

    public void Stop()
    {
        if (_processHost is not null)
        {
            _processHost.PreviewUpdated -= OnProcessHostPreviewUpdated;
        }

        _activeCanContinue = null;
        _activeOnChanged = null;

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

    private void OnProcessHostPreviewUpdated(Guid jobId, string previewPath)
    {
        var onChanged = _activeOnChanged;
        var canContinue = _activeCanContinue;
        if (onChanged is not null && canContinue?.Invoke() == true)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await onChanged().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Push-based preview refresh failed");
                }
            });
        }
    }

    public void Dispose() => Stop();

    public void Reset() => _lastPreviewCheck = DateTime.MinValue;

    public bool RefreshPreviewImages(string? outputRoot, ObservableCollection<string> previewImagePaths, bool force)
    {
        var previewDir = string.IsNullOrWhiteSpace(outputRoot) ? null : Path.Combine(outputRoot, "preview");
        if (string.IsNullOrWhiteSpace(previewDir) || !Directory.Exists(previewDir))
        {
            if (previewImagePaths.Count > 0)
            {
                previewImagePaths.Clear();
                _lastPreviewCheck = DateTime.MinValue;
                return true;
            }

            _lastPreviewCheck = DateTime.MinValue;
            return false;
        }

        var files = _previewService.GetPreviewImagePaths(outputRoot).ToList();
        var lastWrite = files.Count > 0 ? files.Max(File.GetLastWriteTimeUtc) : DateTime.MinValue;
        // Same file names (preview_0.jpg…) are overwritten every epoch. Detect content
        // refresh by mtime, then rebuild the collection so ItemsControl rebinds images.
        if (!force
            && files.Count == previewImagePaths.Count
            && lastWrite <= _lastPreviewCheck
            && files.SequenceEqual(previewImagePaths, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        previewImagePaths.Clear();
        foreach (var file in files)
        {
            previewImagePaths.Add(file);
        }

        _lastPreviewCheck = lastWrite;
        return true;
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
