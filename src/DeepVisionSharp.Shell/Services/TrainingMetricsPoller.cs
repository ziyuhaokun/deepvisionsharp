using Microsoft.Extensions.Logging;

using System.IO;
using System.Text;
using System.Threading;

using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.Services;

public sealed class TrainingMetricsPoller : IDisposable
{
    private readonly TrainingMetricsService _metricsService = new();
    private readonly ILogger<TrainingMetricsPoller> _logger;
    private readonly Dictionary<Guid, List<TrainingMetricPoint>> _jobMetrics = new();
    private readonly object _metricsLock = new();
    private readonly object _metricsReadLock = new();
    private CancellationTokenSource? _cts;
    private Task? _runningTask;
    private Action<Exception>? _onError;
    private bool _errorReported;
    private long _metricsReadOffset;
    private string _metricsPending = string.Empty;
    private Guid _metricsReadJobId;

    public TrainingMetricsPoller(ILogger<TrainingMetricsPoller> logger)
    {
        _logger = logger;
    }

    public void Start(
        Guid jobId,
        string logFile,
        Func<bool> canContinue,
        Func<Task> onChanged,
        Action<Exception> onError,
        bool fullReload)
    {
        Stop();
        _onError = onError;
        _errorReported = false;
        _cts = new CancellationTokenSource();
        _runningTask = RunObservedAsync(jobId, logFile, canContinue, onChanged, fullReload, _cts.Token);
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

    public void ClearMetrics(Guid jobId)
    {
        lock (_metricsLock)
        {
            _jobMetrics.Remove(jobId);
        }

        lock (_metricsReadLock)
        {
            _metricsReadOffset = 0;
            _metricsPending = string.Empty;
            _metricsReadJobId = Guid.Empty;
        }
    }

    public IReadOnlyList<TrainingMetricPoint> GetMetrics(Guid jobId)
    {
        lock (_metricsLock)
        {
            return _jobMetrics.TryGetValue(jobId, out var metrics) ? metrics.ToList() : [];
        }
    }

    public bool TryParseLiveMetric(string line, Guid jobId, out TrainingMetricPoint? point)
    {
        point = null;
        if (!_metricsService.TryParseMetricLine(line, out var metric))
        {
            return false;
        }

        lock (_metricsLock)
        {
            if (!_jobMetrics.TryGetValue(jobId, out var series))
            {
                series = [];
                _jobMetrics[jobId] = series;
            }

            if (TrainingMetricsService.UpsertMetric(series, metric))
            {
                point = metric;
                return true;
            }
        }

        return false;
    }

    public bool EnsureMetricsLoaded(Guid jobId, string logFile, bool fullReload)
    {
        try
        {
            return LoadMetricsFromLog(jobId, logFile, fullReload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load training metrics from log for job {JobId}", jobId);
            return false;
        }
    }

    public bool LoadMetricsFromLog(Guid jobId, string logFile, bool fullReload)
    {
        if (string.IsNullOrWhiteSpace(logFile) || !File.Exists(logFile))
        {
            return false;
        }

        // The read offset and the partial-line buffer describe "how far through this log we are", so
        // the read and the offset update must be one step. Two callers run concurrently by design
        // (the 1s timer and the UI thread's full reload); without this lock both read the same
        // offset and append the same chunk twice, or splice halves of different reads together.
        lock (_metricsReadLock)
        {
            return LoadMetricsFromLogCore(jobId, logFile, fullReload);
        }
    }

    private bool LoadMetricsFromLogCore(Guid jobId, string logFile, bool fullReload)
    {
        using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // The offset belongs to one log file. Switching jobs must restart from scratch, otherwise a
        // smaller new log would be read from a stale, larger offset and report nothing.
        if (_metricsReadJobId != jobId)
        {
            _metricsReadJobId = jobId;
            fullReload = true;
        }

        if (fullReload)
        {
            _metricsReadOffset = Math.Max(0, stream.Length - 1024 * 1024);
            _metricsPending = string.Empty;
        }
        else if (_metricsReadOffset > stream.Length)
        {
            _metricsReadOffset = 0;
        }

        stream.Seek(_metricsReadOffset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        var text = reader.ReadToEnd();
        _metricsReadOffset = stream.Position;
        if (string.IsNullOrEmpty(text) && !fullReload)
        {
            return false;
        }

        var combined = _metricsPending + text;
        var lines = combined.Split('\n');
        _metricsPending = combined.EndsWith('\n') ? string.Empty : lines.LastOrDefault() ?? string.Empty;
        var changed = false;
        lock (_metricsLock)
        {
            if (!_jobMetrics.TryGetValue(jobId, out var series))
            {
                series = [];
                _jobMetrics[jobId] = series;
            }

            var count = combined.EndsWith('\n') ? lines.Length : Math.Max(0, lines.Length - 1);
            for (var i = 0; i < count; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (_metricsService.TryParseMetricLine(line, out var metric))
                {
                    changed |= TrainingMetricsService.UpsertMetric(series, metric);
                }
            }
        }

        return changed;
    }

    private async Task RunObservedAsync(
        Guid jobId,
        string logFile,
        Func<bool> canContinue,
        Func<Task> onChanged,
        bool fullReload,
        CancellationToken token)
    {
        try
        {
            await RunAsync(jobId, logFile, canContinue, onChanged, fullReload, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Training metrics polling stopped unexpectedly for job {JobId}", jobId);
            ReportError(ex);
        }
    }

    private async Task RunAsync(
        Guid jobId,
        string logFile,
        Func<bool> canContinue,
        Func<Task> onChanged,
        bool fullReload,
        CancellationToken token)
    {
        if (LoadMetricsFromLog(jobId, logFile, fullReload))
        {
            await onChanged().ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            if (!canContinue() || !File.Exists(logFile))
            {
                continue;
            }

            if (LoadMetricsFromLog(jobId, logFile, fullReload: false))
            {
                await onChanged().ConfigureAwait(false);
            }
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

        _logger.LogWarning(task.Exception, "Training metrics polling task faulted while stopping.");
    }
}
