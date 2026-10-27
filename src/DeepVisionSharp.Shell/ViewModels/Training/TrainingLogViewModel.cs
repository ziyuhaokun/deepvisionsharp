using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Windows.Threading;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class TrainingLogViewModel : ObservableObject, IDisposable
{
    private readonly ITrainingProcessHost _processHost;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IClipboardService _clipboard;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<TrainingLogViewModel> _logger;
    private readonly DispatcherTimer _tailTimer;
    private readonly DispatcherTimer _flushTimer;
    private readonly StringBuilder _liveLogBuilder = new();
    private bool _liveLogDirty;
    private Guid? _trackedJobId;
    private string? _trackedLogFile;
    private long _logFileOffset;
    private bool _isRunning;
    private bool _processSubscribed;
    private long _trackingGeneration;
    private readonly SemaphoreSlim _tailReadGate = new(1, 1);

    public TrainingLogViewModel(
        ITrainingProcessHost processHost,
        ShellSnackbarNotifier snackbar,
        IClipboardService clipboard,
        IUiDispatcher uiDispatcher,
        ILogger<TrainingLogViewModel> logger)
    {
        _processHost = processHost;
        _snackbar = snackbar;
        _clipboard = clipboard;
        _uiDispatcher = uiDispatcher;
        _logger = logger;

        _processHost.LogReceived += OnLogReceived;
        _processSubscribed = true;

        // DispatcherTimer is a WPF UI timer; kept here because it must tick on the UI thread.
        _tailTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tailTimer.Tick += OnTailTimerTick;

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _flushTimer.Tick += OnFlushTimerTick;
    }

    public event Action<string>? LineAppended;

    [ObservableProperty]
    private string _liveLog = string.Empty;

    [ObservableProperty]
    private bool _autoScrollLog = true;

    public bool HasLogContent => _liveLogBuilder.Length > 0;

    public bool IsTracking(Guid jobId) => _trackedJobId == jobId;

    public void StartTracking(Guid jobId, string? logFile, bool isRunning)
    {
        Interlocked.Increment(ref _trackingGeneration);
        _trackedJobId = jobId;
        _trackedLogFile = logFile;
        _logFileOffset = 0;
        _liveLogBuilder.Clear();
        LiveLog = string.Empty;
        OnPropertyChanged(nameof(HasLogContent));

        if (!string.IsNullOrWhiteSpace(logFile) && File.Exists(logFile))
        {
            TailLogFile(initialLoad: true);
        }
        else
        {
            ReplayRecentHostLogs(jobId);
        }

        UpdateRunningState(isRunning);
    }

    public void StopTracking()
    {
        Interlocked.Increment(ref _trackingGeneration);
        _trackedJobId = null;
        _trackedLogFile = null;
        _logFileOffset = 0;
        _tailTimer.Stop();
        _flushTimer.Stop();
        _liveLogDirty = false;
        _isRunning = false;
    }

    public void UpdateRunningState(bool isRunning)
    {
        _isRunning = isRunning;
        UpdateTailTimerState();
    }

    public void TailCurrentLogOnDemand()
    {
        if (string.IsNullOrWhiteSpace(_trackedLogFile) || ShouldUseStreamLogs())
        {
            return;
        }

        TailLogFile(initialLoad: true);
    }

    public void FlushToUi(bool force = false)
    {
        if (!_liveLogDirty && !force)
        {
            _flushTimer.Stop();
            return;
        }

        _liveLogDirty = false;
        _flushTimer.Stop();
        LiveLog = _liveLogBuilder.ToString();
    }

    public void ClearLogContent()
    {
        _liveLogBuilder.Clear();
        _liveLogDirty = false;
        _flushTimer.Stop();
        LiveLog = string.Empty;
        OnPropertyChanged(nameof(HasLogContent));

        if (!string.IsNullOrWhiteSpace(_trackedLogFile) && File.Exists(_trackedLogFile))
        {
            _logFileOffset = new FileInfo(_trackedLogFile).Length;
        }
    }

    public void AppendLogLine(string line)
    {
        if (line.Length == 0 && _liveLogBuilder.Length == 0)
        {
            return;
        }

        if (_liveLogBuilder.Length > 0)
        {
            _liveLogBuilder.AppendLine();
        }

        _liveLogBuilder.Append(line);

        if (_liveLogBuilder.Length > 200_000)
        {
            _liveLogBuilder.Remove(0, _liveLogBuilder.Length - 120_000);
        }

        LineAppended?.Invoke(line);
        OnPropertyChanged(nameof(HasLogContent));
        ScheduleFlush();
    }

    /// <summary>页面重新进入时恢复训练进程日志订阅（页面实例复用场景）；幂等。</summary>
    public void Activate()
    {
        if (_processSubscribed)
        {
            return;
        }

        _processSubscribed = true;
        _processHost.LogReceived += OnLogReceived;
    }

    /// <summary>页面卸载时退订训练进程日志，断开 DI 单例对 VM 图的持有；幂等。</summary>
    public void Deactivate()
    {
        if (!_processSubscribed)
        {
            return;
        }

        _processSubscribed = false;
        Interlocked.Increment(ref _trackingGeneration);
        _processHost.LogReceived -= OnLogReceived;
    }

    public void Dispose()
    {
        Deactivate();
        _tailTimer.Stop();
        _tailTimer.Tick -= OnTailTimerTick;
        _flushTimer.Stop();
        _flushTimer.Tick -= OnFlushTimerTick;
    }

    [RelayCommand]
    private void ClearLog() => ClearLogContent();

    [RelayCommand]
    private void CopyLog()
    {
        FlushToUi(force: true);
        var text = _liveLogBuilder.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            _clipboard.SetText(text);
            _snackbar.ShowSuccess("日志已复制到剪贴板。", "训练");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy training log");
            _snackbar.ShowError("复制日志失败。", "训练");
        }
    }

    private bool ShouldUseStreamLogs()
        => _trackedJobId is Guid jobId && _processHost.IsActive(jobId);

    private void OnLogReceived(Guid jobId, string line)
    {
        var generation = Volatile.Read(ref _trackingGeneration);
        if (_trackedJobId != jobId)
        {
            return;
        }

        _uiDispatcher.Post(() =>
        {
            if (_processSubscribed && _trackedJobId == jobId
                && generation == Volatile.Read(ref _trackingGeneration))
            {
                AppendLogLine(line);
            }
        });
    }

    private void OnTailTimerTick(object? sender, EventArgs e)
    {
        if (ShouldUseStreamLogs())
        {
            return;
        }

        ShellAsync.FireAndForget(
            () => TailLogFileAsync(),
            _logger,
            "TailLogFile");
    }

    private void OnFlushTimerTick(object? sender, EventArgs e)
    {
        FlushToUi();
    }

    private void ScheduleFlush()
    {
        _liveLogDirty = true;
        if (!_flushTimer.IsEnabled)
        {
            _flushTimer.Start();
        }
    }

    private void UpdateTailTimerState()
    {
        if (ShouldUseStreamLogs())
        {
            _tailTimer.Stop();
            return;
        }

        if (_isRunning)
        {
            _tailTimer.Start();
            return;
        }

        _tailTimer.Stop();
    }

    private void TailLogFile(bool initialLoad = false)
    {
        ShellAsync.FireAndForget(
            () => TailLogFileAsync(initialLoad),
            _logger,
            "TailLogFile");
    }

    private async Task TailLogFileAsync(bool initialLoad = false)
    {
        if (string.IsNullOrWhiteSpace(_trackedLogFile) || !File.Exists(_trackedLogFile))
        {
            return;
        }

        // Read and offset update are one step. Two callers run by design (the 1s timer and the
        // on-demand tail when switching to the log tab); without serialising them both read the same
        // offset and append the same lines twice.
        await _tailReadGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = _trackedLogFile;
            var offset = _logFileOffset;
            var generation = Volatile.Read(ref _trackingGeneration);

            var result = await Task.Run(() => ReadLogTailChunk(path, offset, initialLoad)).ConfigureAwait(false);
            if (result is null || string.IsNullOrEmpty(result.Value.Chunk))
            {
                return;
            }

            var chunk = result.Value.Chunk;
            var newOffset = result.Value.NewOffset;

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (generation != Volatile.Read(ref _trackingGeneration)
                    || !string.Equals(path, _trackedLogFile, StringComparison.Ordinal))
                {
                    return;
                }

                _logFileOffset = newOffset;
                AppendLogChunk(chunk);
            }).ConfigureAwait(false);
        }
        finally
        {
            _tailReadGate.Release();
        }
    }

    private static (string Chunk, long NewOffset)? ReadLogTailChunk(string path, long offset, bool initialLoad)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (offset > stream.Length)
        {
            offset = 0;
        }

        stream.Seek(offset, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var chunk = reader.ReadToEnd();
        var newOffset = stream.Length;

        if (string.IsNullOrEmpty(chunk))
        {
            return null;
        }

        if (initialLoad && chunk.Length > 120_000)
        {
            chunk = chunk[^120_000..];
        }

        return (chunk, newOffset);
    }

    private void ReplayRecentHostLogs(Guid jobId)
    {
        foreach (var line in _processHost.GetRecentLogLines(jobId))
        {
            AppendLogLine(line);
        }
    }

    private void AppendLogChunk(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        var lines = chunk.Split(["\r\n", "\n"], StringSplitOptions.None);
        foreach (var line in lines)
        {
            AppendLogLine(line);
        }
    }
}
