using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Crash breadcrumb for an in-process training run. The file lives in the job output root,
/// is refreshed by <see cref="TrainingRunHeartbeat"/> and removed on every normal exit path.
/// A leftover file therefore means the process died while the job was running.
/// </summary>
internal sealed record TrainingRunMarker(
    int SchemaVersion,
    Guid JobId,
    int ProcessId,
    DateTime StartedUtc,
    string Stage,
    DateTime HeartbeatUtc,
    int? Epoch,
    int? TotalEpochs,
    string? Message);

internal static class TrainingRunMarkerStore
{
    public const int CurrentSchemaVersion = 1;
    public const string FileName = ".deepvision-training.json";

    public const string StagePreparing = "preparing";
    public const string StageExporting = "exporting";
    public const string StageTraining = "training";
    public const string StageSaving = "saving";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string? GetPath(string? outputRoot) =>
        string.IsNullOrWhiteSpace(outputRoot) ? null : Path.Combine(outputRoot, FileName);

    public static void Write(string? outputRoot, TrainingRunMarker marker, ILogger? logger = null)
    {
        var path = GetPath(outputRoot);
        if (path is null)
        {
            return;
        }

        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(marker, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Unable to write training run marker at {Path}", path);
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best-effort cleanup of the staging file.
            }
        }
    }

    public static TrainingRunMarker? TryRead(string? outputRoot, ILogger? logger = null)
    {
        var path = GetPath(outputRoot);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var marker = JsonSerializer.Deserialize<TrainingRunMarker>(File.ReadAllText(path), JsonOptions);
            return marker is null || marker.JobId == Guid.Empty ? null : marker;
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Unable to read training run marker at {Path}", path);
            return null;
        }
    }

    public static void Delete(string? outputRoot, ILogger? logger = null)
    {
        var path = GetPath(outputRoot);
        if (path is null)
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Unable to delete training run marker at {Path}", path);
        }
    }

    /// <summary>
    /// Failure reason shown for an orphaned job. Uses the breadcrumb of this job when present,
    /// otherwise the generic restart message.
    /// </summary>
    public static string BuildInterruptionMessage(
        TrainingRunMarker? marker,
        Guid jobId,
        string fallback)
    {
        if (marker is null || marker.JobId != jobId)
        {
            return fallback;
        }

        var stage = DescribeStage(marker.Stage);
        var progress = marker.Epoch is int epoch && marker.TotalEpochs is int total && total > 0
            ? $"，epoch {epoch}/{total}"
            : string.Empty;

        return $"应用重启导致 TorchSharp 分类训练中断（阶段：{stage}{progress}，最后心跳 "
               + $"{marker.HeartbeatUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}）。请复制为新任务后重试；"
               + "若进程异常退出，请在设置页的崩溃转储目录与训练日志中查找现场。";
    }

    private static string DescribeStage(string? stage) => stage switch
    {
        StagePreparing => "准备",
        StageExporting => "导出数据集",
        StageTraining => "模型训练",
        StageSaving => "保存权重",
        _ when string.IsNullOrWhiteSpace(stage) => "未知",
        _ => stage
    };
}

/// <summary>
/// Periodically persists the current stage/epoch of an in-process run so a crash leaves
/// enough context for the startup recovery pass.
/// </summary>
internal sealed class TrainingRunHeartbeat : IDisposable
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly Guid _jobId;
    private readonly string? _outputRoot;
    private readonly ILogger? _logger;
    private readonly DateTime _startedUtc = DateTime.UtcNow;
    private readonly Timer _timer;
    private string _stage = TrainingRunMarkerStore.StagePreparing;
    private int? _epoch;
    private int? _totalEpochs;
    private string? _message;
    private int _disposed;

    public TrainingRunHeartbeat(Guid jobId, string? outputRoot, ILogger? logger = null)
    {
        _jobId = jobId;
        _outputRoot = outputRoot;
        _logger = logger;
        _timer = new Timer(_ => FlushNow(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        FlushNow();
        _timer.Change(FlushInterval, FlushInterval);
    }

    public void SetStage(string stage)
    {
        lock (_gate)
        {
            _stage = stage;
        }

        FlushNow();
    }

    public void ReportProgress(int epoch, int totalEpochs, string? message)
    {
        lock (_gate)
        {
            _epoch = epoch;
            _totalEpochs = totalEpochs;
            _message = message;
        }
    }

    public void FlushNow()
    {
        lock (_gate)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            var marker = new TrainingRunMarker(
                TrainingRunMarkerStore.CurrentSchemaVersion,
                _jobId,
                Environment.ProcessId,
                _startedUtc,
                _stage,
                DateTime.UtcNow,
                _epoch,
                _totalEpochs,
                _message);
            TrainingRunMarkerStore.Write(_outputRoot, marker, _logger);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _timer.Dispose();

        // Wait for any in-flight flush, then remove the breadcrumb of this normal exit.
        lock (_gate)
        {
            TrainingRunMarkerStore.Delete(_outputRoot, _logger);
        }
    }
}
