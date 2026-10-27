using System.Collections.Concurrent;
using System.IO;

using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>Legacy global lock kept for callers that still coordinate with file writes.</summary>
    public static readonly object FileLock = new();

    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly FileLoggerOptions _options;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        _options = options;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _options);
    }

    public void Dispose()
    {
    }

    /// <summary>
    /// Thread-safe append. Creates parent directories. Never throws to callers that wrap it;
    /// callers may still catch. Uses per-path locks to reduce multi-stage contention.
    /// </summary>
    public static void AppendToFile(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var gate = PathLocks.GetOrAdd(path, static _ => new object());
        lock (gate)
        {
            File.AppendAllText(path, content);
        }
    }

    public static void WriteAllText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var gate = PathLocks.GetOrAdd(path, static _ => new object());
        lock (gate)
        {
            File.WriteAllText(path, content);
        }
    }
}

public sealed class FileLoggerOptions
{
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;

    public string LogDirectory { get; init; } = Services.ShellRuntimePaths.Current.LogDirectory;

    /// <summary>When true, also append every line to Logs/all/yyyy-MM-dd.log. Default off.</summary>
    public bool EnableAllMirror { get; init; }

    /// <summary>
    /// Days to retain log files, counted from the date in the file name; 0 disables cleanup.
    /// Enforced by <see cref="LogRetentionService"/> at startup and daily thereafter.
    /// </summary>
    public int RetentionDays { get; init; } = 14;

    /// <summary>
    /// Upper bound for the whole log directory. Once expired files are gone, the oldest remaining
    /// files are removed until the directory fits. 0 disables the size cap.
    /// </summary>
    public long MaxTotalBytes { get; init; } = 256L * 1024 * 1024;

    public string GetStageLogPath(LogStage stage, DateTimeOffset timestamp) =>
        Path.Combine(LogDirectory, stage.ToDirectoryName(), $"{timestamp:yyyy-MM-dd}.log");

    public string GetDailyExceptionLogPath(DateTimeOffset timestamp) =>
        Path.Combine(LogDirectory, "exceptions", $"{timestamp:yyyy-MM-dd}-exceptions.log");

    public string GetAllMirrorLogPath(DateTimeOffset timestamp) =>
        Path.Combine(LogDirectory, "all", $"{timestamp:yyyy-MM-dd}.log");

    /// <summary>Crash dump text at log root (not under a stage folder).</summary>
    public string GetCrashLatestPath(DateTimeOffset timestamp) =>
        Path.Combine(LogDirectory, $"{timestamp:yyyy-MM-dd}-crash-latest.txt");

    /// <summary>
    /// App-stage daily log. Preferred for unhandled-exception summaries written outside ILogger.
    /// </summary>
    public string GetAppDailyLogPath(DateTimeOffset timestamp) =>
        GetStageLogPath(LogStage.App, timestamp);
}
