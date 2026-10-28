using System.Text;
using System.Windows;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Shell.Logging;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Global exception surface: always log, keep the process alive when possible,
/// and offer to copy a full crash report to the clipboard.
/// </summary>
public sealed class UnhandledExceptionReporter
{
    private static readonly object PromptGate = new();
    private static int _dialogDepth;
    private static DateTimeOffset _lastPromptUtc = DateTimeOffset.MinValue;
    private static string? _lastPromptSignature;

    private readonly ILogger<UnhandledExceptionReporter> _logger;
    private readonly FileLoggerOptions _logOptions = new();

    public UnhandledExceptionReporter(ILogger<UnhandledExceptionReporter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Report without DI (startup / last-chance). Safe to call from any thread.
    /// </summary>
    public static void ReportStatic(Exception exception, string source, bool promptUser = true)
    {
        if (IsCancellation(exception))
        {
            return;
        }

        try
        {
            ShellExceptionJournal.Record(exception, source);
            var options = new FileLoggerOptions();
            var report = BuildReport(exception, source, options);
            WriteReportFiles(report, options, exception, source);
            if (promptUser && !ShellRuntimePaths.Current.IsVerification)
            {
                PromptCopyToClipboard(report, options);
            }
        }
        catch
        {
            // Last chance: never throw from the exception path.
        }
    }

    public void Report(Exception exception, string source, bool showMessage = true)
    {
        if (IsCancellation(exception))
        {
            return;
        }

        try { ShellExceptionJournal.Record(exception, source); }
        catch { /* Exit code/count still record failure if writing the journal failed. */ }
        try
        {
            _logger.LogError(exception, "Unhandled exception captured from {Source}", source);
        }
        catch
        {
            // Logging must not throw.
        }

        string report;
        try
        {
            report = BuildReport(exception, source, _logOptions);
            WriteReportFiles(report, _logOptions, exception, source);
        }
        catch
        {
            report = $"{source}: {exception}";
        }

        if (!showMessage || ShellRuntimePaths.Current.IsVerification)
        {
            return;
        }

        PromptCopyToClipboard(report, _logOptions);
    }

    private static bool IsCancellation(Exception? ex)
    {
        if (ex is null) return false;
        if (ex is OperationCanceledException or TaskCanceledException) return true;
        if (ex is AggregateException agg)
        {
            var flat = agg.Flatten();
            return flat.InnerExceptions.Count > 0 && flat.InnerExceptions.All(inner => inner is OperationCanceledException or TaskCanceledException);
        }
        return false;
    }

    private static string BuildReport(Exception exception, string source, FileLoggerOptions options)
    {
        var now = DateTimeOffset.Now;
        var sb = new StringBuilder(4096);
        sb.AppendLine("DeepVisionSharp 异常报告");
        sb.AppendLine(new string('=', 72));
        sb.AppendLine($"时间: {now:yyyy-MM-dd HH:mm:ss.fff zzz}");
        sb.AppendLine($"来源: {source}");
        sb.AppendLine($"进程: {Environment.ProcessId}");
        sb.AppendLine($"版本: {typeof(UnhandledExceptionReporter).Assembly.GetName().Version}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine($"64-bit process: {Environment.Is64BitProcess}");
        sb.AppendLine($"日志目录: {options.LogDirectory}");
        sb.AppendLine($"App 日志: {options.GetAppDailyLogPath(now)}");
        sb.AppendLine($"异常日志: {options.GetDailyExceptionLogPath(now)}");
        sb.AppendLine($"崩溃报告: {options.GetCrashLatestPath(now)}");
        sb.AppendLine($"崩溃转储: {ShellRuntimePaths.Current.CrashDumpDirectory}");
        sb.AppendLine("分 stage 目录: app / training / data / model / inference / imaging / system / exceptions");
        sb.AppendLine(new string('-', 72));
        sb.AppendLine($"异常类型: {exception.GetType().FullName}");
        sb.AppendLine($"消息: {exception.Message}");
        sb.AppendLine();
        sb.AppendLine("完整堆栈:");
        sb.AppendLine(exception.ToString());

        if (exception is AggregateException aggregate)
        {
            var flat = aggregate.Flatten();
            var index = 0;
            foreach (var inner in flat.InnerExceptions)
            {
                sb.AppendLine();
                sb.AppendLine($"--- Inner[{index++}]: {inner.GetType().FullName} ---");
                sb.AppendLine(inner.ToString());
            }
        }

        sb.AppendLine(new string('=', 72));
        return sb.ToString();
    }

    private static void WriteReportFiles(
        string report,
        FileLoggerOptions options,
        Exception exception,
        string source)
    {
        try
        {
            var now = DateTimeOffset.Now;
            var crashPath = options.GetCrashLatestPath(now);
            var summaryLine =
                $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [CRT] [App] UnhandledExceptionReporter: {source}: {exception.Message}{Environment.NewLine}{exception}{Environment.NewLine}";

            FileLoggerProvider.WriteAllText(crashPath, report);
            FileLoggerProvider.AppendToFile(
                options.GetDailyExceptionLogPath(now),
                report + Environment.NewLine);
            FileLoggerProvider.AppendToFile(options.GetAppDailyLogPath(now), summaryLine);
        }
        catch
        {
            // Ignore disk failures.
        }
    }

    private static void PromptCopyToClipboard(string report, FileLoggerOptions options)
    {
        // Debounce identical bursts (e.g. repeated binding failures).
        var signature = report.Length > 240 ? report[..240] : report;
        lock (PromptGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (string.Equals(signature, _lastPromptSignature, StringComparison.Ordinal)
                && now - _lastPromptUtc < TimeSpan.FromSeconds(3))
            {
                return;
            }

            if (_dialogDepth > 0)
            {
                return;
            }

            _lastPromptSignature = signature;
            _lastPromptUtc = now;
            _dialogDepth++;
        }

        try
        {
            void Show()
            {
                try
                {
                    var logHint = options.LogDirectory;
                    var result = System.Windows.MessageBox.Show(
                        "程序遇到未处理异常，但已尝试拦截以继续运行。\n\n"
                        + "详细信息已写入日志目录：\n"
                        + logHint
                        + "\n\n是否将本次报错日志复制到剪贴板？",
                        "DeepVisionSharp — 发生异常",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error,
                        MessageBoxResult.No);

                    if (result == MessageBoxResult.Yes)
                    {
                        TrySetClipboardText(report);
                    }
                }
                catch
                {
                    // Dialog/clipboard must never rethrow into the exception path.
                }
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                Show();
                return;
            }

            if (dispatcher.CheckAccess())
            {
                Show();
            }
            else
            {
                // Prefer synchronous invoke so AppDomain terminating crashes still show a prompt.
                try
                {
                    dispatcher.Invoke(DispatcherPriority.Send, Show);
                }
                catch
                {
                    try
                    {
                        dispatcher.BeginInvoke(DispatcherPriority.Send, Show);
                    }
                    catch
                    {
                        Show();
                    }
                }
            }
        }
        finally
        {
            lock (PromptGate)
            {
                _dialogDepth = Math.Max(0, _dialogDepth - 1);
            }
        }
    }

    private static void TrySetClipboardText(string text)
    {
        try
        {
            // copy:false so the report does not remain after this process exits.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(text, copy: false);
                    return;
                }
                catch when (attempt < 2)
                {
                    Thread.Sleep(40);
                }
            }
        }
        catch
        {
            try
            {
                System.Windows.MessageBox.Show(
                    "复制到剪贴板失败。请到日志目录手动打开 exceptions/*-exceptions.log 或 *-crash-latest.txt。",
                    "DeepVisionSharp",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
                // ignore
            }
        }
    }
}
