using System.Collections.Concurrent;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

public sealed class TrainingProcessHost : ITrainingProcessHost, IDisposable
{
    private const int MaxRecentLogLines = 500;

    private readonly ConcurrentDictionary<Guid, byte> _activeSessions = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<string>> _recentLogs = new();

    public event Action<Guid, string>? LogReceived;
    public event Action<Guid, string>? PreviewUpdated;

    public bool IsActive(Guid jobId) => _activeSessions.ContainsKey(jobId);

    public IDisposable BeginExternalLogSession(Guid jobId)
    {
        if (!_activeSessions.TryAdd(jobId, 0))
        {
            throw new InvalidOperationException($"训练任务 {jobId} 已有外部日志会话。");
        }

        ResetJobLogBuffers(jobId);
        return new ExternalLogSession(this, jobId);
    }

    public void PublishLog(Guid jobId, string line)
    {
        RecordLog(jobId, line, fromError: false);
    }

    public void PublishPreviewUpdated(Guid jobId, string previewImagePath)
    {
        if (string.IsNullOrWhiteSpace(previewImagePath)) return;
        PreviewUpdated?.Invoke(jobId, previewImagePath);
    }

    public IReadOnlyList<string> GetRecentLogLines(Guid jobId)
    {
        if (!_recentLogs.TryGetValue(jobId, out var queue))
        {
            return Array.Empty<string>();
        }

        return queue.ToArray();
    }

    public void Dispose()
    {
        _activeSessions.Clear();
        _recentLogs.Clear();
    }

    private void EndExternalLogSession(Guid jobId) => _activeSessions.TryRemove(jobId, out _);

    private void ResetJobLogBuffers(Guid jobId)
    {
        _recentLogs[jobId] = new ConcurrentQueue<string>();
    }

    private void RecordLog(Guid jobId, string? line, bool fromError)
    {
        if (line is null)
        {
            return;
        }

        var display = fromError && !line.StartsWith("[ERR]", StringComparison.Ordinal)
            ? (line.Length == 0 ? "[ERR]" : "[ERR] " + line)
            : line;

        AppendRecentLog(jobId, display);
        LogReceived?.Invoke(jobId, display);
    }

    private void AppendRecentLog(Guid jobId, string line)
    {
        var queue = _recentLogs.GetOrAdd(jobId, _ => new ConcurrentQueue<string>());
        queue.Enqueue(line);
        while (queue.Count > MaxRecentLogLines && queue.TryDequeue(out _))
        {
        }
    }

    private sealed class ExternalLogSession : IDisposable
    {
        private readonly TrainingProcessHost _host;
        private readonly Guid _jobId;
        private int _disposed;

        public ExternalLogSession(TrainingProcessHost host, Guid jobId)
        {
            _host = host;
            _jobId = jobId;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _host.EndExternalLogSession(_jobId);
        }
    }
}
