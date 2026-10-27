using System.Text;

using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Logging;

internal sealed class FileLogger : ILogger
{
    private readonly string _categoryName;
    private readonly FileLoggerOptions _options;
    private readonly LogStage _stage;

    public FileLogger(string categoryName, FileLoggerOptions options)
    {
        _categoryName = categoryName;
        _options = options;
        _stage = LogStageResolver.Resolve(categoryName);
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _options.MinimumLogLevel;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        var timestamp = DateTimeOffset.Now;
        var timestampText = timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
        var levelText = FormatLevel(logLevel);
        var threadId = Environment.CurrentManagedThreadId;
        var line =
            $"{timestampText} [{levelText}] [{_stage.ToLabel()}] T{threadId} {_categoryName}: {message}";

        try
        {
            var mainContent = exception is null
                ? line + Environment.NewLine
                : line + Environment.NewLine + exception + Environment.NewLine;

            FileLoggerProvider.AppendToFile(_options.GetStageLogPath(_stage, timestamp), mainContent);

            if (_options.EnableAllMirror)
            {
                FileLoggerProvider.AppendToFile(_options.GetAllMirrorLogPath(timestamp), mainContent);
            }

            if (exception is not null)
            {
                AppendExceptionEntry(timestamp, timestampText, levelText, message, exception);
            }
        }
        catch
        {
            // Logging must never become the reason the desktop app crashes.
        }
    }

    private void AppendExceptionEntry(
        DateTimeOffset timestamp,
        string timestampText,
        string levelText,
        string message,
        Exception exception)
    {
        var entry = new StringBuilder();
        entry.AppendLine(new string('=', 80));
        entry.AppendLine($"{timestampText} [{levelText}] [{_stage.ToLabel()}] {_categoryName}");
        entry.AppendLine($"Message: {message}");
        entry.AppendLine($"Exception: {exception.GetType().FullName}");
        entry.AppendLine(exception.ToString());
        entry.AppendLine(new string('=', 80));
        entry.AppendLine();

        FileLoggerProvider.AppendToFile(_options.GetDailyExceptionLogPath(timestamp), entry.ToString());
    }

    private static string FormatLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        LogLevel.None => "NON",
        _ => "INF",
    };

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
