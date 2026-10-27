namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// In-process training log stream port (channel B): job activity and log fan-out.
/// Not a MediatR request; used by training backends and Shell monitors.
/// </summary>
public interface ITrainingProcessHost
{
    bool IsActive(Guid jobId);

    /// <summary>
    /// Mark an in-process job as active for log streaming (IsActive + LogReceived).
    /// Dispose when the session ends.
    /// </summary>
    IDisposable BeginExternalLogSession(Guid jobId);

    /// <summary>Push a log line to Shell monitors for an active job.</summary>
    void PublishLog(Guid jobId, string line);

    /// <summary>Notify subscribers that a new validation preview image has been generated.</summary>
    void PublishPreviewUpdated(Guid jobId, string previewImagePath);

    /// <summary>返回该任务自本次启动以来的最近日志行，供训练页重新进入后回放。</summary>
    IReadOnlyList<string> GetRecentLogLines(Guid jobId);

    event Action<Guid, string>? LogReceived;

    /// <summary>Event-driven push notification when a preview image has been generated/updated.</summary>
    event Action<Guid, string>? PreviewUpdated;
}
