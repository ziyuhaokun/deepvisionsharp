using DeepVisionSharp.Presentation.Services;

using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Presentation.Logging;

/// <summary>
/// File logging options. Owned here because settings ViewModels read the retention budget; the
/// providers that consume them stay in each shell.
/// </summary>
public sealed class FileLoggerOptions
{
    public LogLevel MinimumLogLevel { get; init; } = LogLevel.Information;

    public string LogDirectory { get; init; } = ShellRuntimePaths.Current.LogDirectory;

    /// <summary>When true, also append every line to Logs/all/yyyy-MM-dd.log. Default off.</summary>
    public bool EnableAllMirror { get; init; }

    /// <summary>
    /// Days to retain log files, counted from the date in the file name; 0 disables cleanup.
    /// Enforced by the shell's log retention service at startup and daily thereafter.
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
