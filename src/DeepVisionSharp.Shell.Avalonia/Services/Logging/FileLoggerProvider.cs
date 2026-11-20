using System.Collections.Concurrent;
using System.IO;
using System.Text;

using DeepVisionSharp.Presentation.Logging;

using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Avalonia.Services.Logging;

/// <summary>
/// 分 stage 的文件日志提供程序：每条记录追加到 <c>Logs/{stage}/yyyy-MM-dd.log</c>，
/// 带异常时同时写 <c>Logs/exceptions/yyyy-MM-dd-exceptions.log</c>。
/// <para>
/// 与 WPF Shell 的 <c>DeepVisionSharp.Shell.Logging.FileLoggerProvider</c> 同构（同一份
/// <see cref="FileLoggerOptions"/> 目录/保留口径），两套 Shell 迁移期并存，暂时各持一份实现；
/// 收口到 Presentation 后这里删掉。
/// </para>
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private static readonly ConcurrentDictionary<string, object> PathLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly FileLoggerOptions _options;

    public FileLoggerProvider(FileLoggerOptions options)
    {
        _options = options;
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _options);

    public void Dispose()
    {
    }

    /// <summary>
    /// 线程安全追加，父目录不存在时自动创建。调用方（异常上报、日志本身）负责吞掉磁盘失败，
    /// 日志写不进去不能变成应用崩溃的理由。
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
