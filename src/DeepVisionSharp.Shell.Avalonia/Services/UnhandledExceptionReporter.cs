using System.Diagnostics;
using System.Text;
using System.Text.Json;

using Avalonia.Threading;

using DeepVisionSharp.Presentation.Logging;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Shell.Avalonia.Services.Logging;

using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 全局异常出口：先落盘（stage 日志 + 异常日志 + 崩溃报告），在能继续跑的时候不杀进程，
/// 并给用户一个把完整报告复制到剪贴板的机会。
/// </summary>
/// <remarks>
/// 对应 WPF Shell 的 <c>UnhandledExceptionReporter</c>；提示方式从 <c>MessageBox.ShowDialog</c>
/// 换成非阻塞的 <see cref="StartupNoticeWindow"/>，UI 线程已经停掉时退到系统消息框。
/// </remarks>
internal sealed class UnhandledExceptionReporter
{
    private static readonly object PromptGate = new();
    private static int _dialogDepth;
    private static DateTimeOffset _lastPromptUtc = DateTimeOffset.MinValue;
    private static string? _lastPromptSignature;

    private readonly ILogger<UnhandledExceptionReporter> _logger;
    private readonly FileLoggerOptions _logOptions;

    public UnhandledExceptionReporter(ILogger<UnhandledExceptionReporter> logger, FileLoggerOptions logOptions)
    {
        _logger = logger;
        _logOptions = logOptions;
    }

    /// <summary>退出码 70 的依据：验证profile 下记录过的失败条数。</summary>
    public static int ReportedCount => UnhandledExceptionJournal.ReportedCount;

    /// <summary>不经过 DI 上报（容器还没建起来 / 最后一道兜底）。任何线程都能调。</summary>
    public static void ReportStatic(Exception exception, string source, bool promptUser = true)
    {
        if (IsCancellation(exception))
        {
            return;
        }

        try
        {
            UnhandledExceptionJournal.Record(exception, source);
            var options = new FileLoggerOptions();
            var report = BuildReport(exception, source, options);
            WriteReportFiles(report, options, exception, source);
            if (promptUser)
            {
                PromptUser(report, options);
            }
        }
        catch (Exception ex)
        {
            // 最后一道：不再向上抛，只能留 trace。
            Trace.WriteLine($"Reporting unhandled exception failed: {ex}");
        }
    }

    public void Report(Exception exception, string source, bool showMessage = true)
    {
        if (IsCancellation(exception))
        {
            return;
        }

        try
        {
            UnhandledExceptionJournal.Record(exception, source);
        }
        catch (Exception ex)
        {
            // 写不了 journal 也要继续写日志；退出码另有 ReportedCount 兜底。
            Trace.WriteLine($"Recording exception journal failed: {ex.Message}");
        }

        try
        {
            _logger.LogError(exception, "Unhandled exception captured from {Source}", source);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Logging unhandled exception failed: {ex.Message}");
        }

        string report;
        try
        {
            report = BuildReport(exception, source, _logOptions);
            WriteReportFiles(report, _logOptions, exception, source);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Writing crash report failed: {ex.Message}");
            report = $"{source}: {exception}";
        }

        if (showMessage)
        {
            PromptUser(report, _logOptions);
        }
    }

    private static bool IsCancellation(Exception? exception)
    {
        switch (exception)
        {
            case null:
                return false;
            case OperationCanceledException or TaskCanceledException:
                return true;
            case AggregateException aggregate:
                var flat = aggregate.Flatten();
                return flat.InnerExceptions.Count > 0
                    && flat.InnerExceptions.All(inner => inner is OperationCanceledException or TaskCanceledException);
            default:
                return false;
        }
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
        sb.AppendLine($"Shell: Avalonia {typeof(UnhandledExceptionReporter).Assembly.GetName().Name}");
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
            var summaryLine =
                $"{now:yyyy-MM-dd HH:mm:ss.fff zzz} [CRT] [App] UnhandledExceptionReporter: {source}: {exception.Message}"
                + Environment.NewLine + exception + Environment.NewLine;

            FileLoggerProvider.WriteAllText(options.GetCrashLatestPath(now), report);
            FileLoggerProvider.AppendToFile(options.GetDailyExceptionLogPath(now), report + Environment.NewLine);
            FileLoggerProvider.AppendToFile(options.GetAppDailyLogPath(now), summaryLine);
        }
        catch (Exception ex)
        {
            // 磁盘写不进去（目录不可写、磁盘满）也不能盖掉原始异常。
            Trace.WriteLine($"Crash report files not written: {ex.Message}");
        }
    }

    /// <summary>同一个提示 3 秒内不重复弹；提示已经开着时不再叠窗口。</summary>
    private static void PromptUser(string report, FileLoggerOptions options)
    {
        if (ShellRuntimePaths.Current.IsVerification)
        {
            return;
        }

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
            ShowCopyPrompt(report, options);
        }
        finally
        {
            lock (PromptGate)
            {
                _dialogDepth = Math.Max(0, _dialogDepth - 1);
            }
        }
    }

    private static void ShowCopyPrompt(string report, FileLoggerOptions options)
    {
        void Show()
        {
            try
            {
                StartupNoticeWindow.ForUnhandledException(options.LogDirectory, report).Show();
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Exception notice window failed: {ex.Message}");
                StartupNativeMessageBox.ShowError(
                    "程序遇到未处理异常。详细信息已写入日志目录：" + Environment.NewLine + options.LogDirectory);
            }
        }

        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Show();
                return;
            }

            // 同步 Invoke（带超时）：AppDomain 正在终止时也要争取把提示弹出来。
            Dispatcher.UIThread.Invoke(Show, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            // Dispatcher 已经停了（启动期崩溃、退出途中），窗口创建不出来。
            Trace.WriteLine($"Exception notice not shown: {ex.Message}");
            StartupNativeMessageBox.ShowError(
                "程序遇到未处理异常。详细信息已写入日志目录：" + Environment.NewLine + options.LogDirectory);
        }
    }
}

/// <summary>
/// 机器可读的失败证据：验证 profile 下每次上报写一行 jsonl，并让进程以 70 退出。
/// 与 WPF Shell 的 <c>ShellExceptionJournal</c> 同一份格式（同一个文件路径来自
/// <see cref="ShellRuntimePaths.ExceptionJournalPath"/>），验收脚本不用区分哪套 Shell。
/// </summary>
internal static class UnhandledExceptionJournal
{
    private static int _reportedCount;

    public static int ReportedCount => Volatile.Read(ref _reportedCount);

    internal static void Record(Exception exception, string source)
    {
        var paths = ShellRuntimePaths.Current;
        if (!paths.IsVerification)
        {
            return;
        }

        Interlocked.Increment(ref _reportedCount);
        // 优雅退出时也要靠它把退出码顶到 70（Program 收口）。
        Environment.ExitCode = 70;
        FileLoggerProvider.AppendToFile(paths.ExceptionJournalPath, JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            processId = Environment.ProcessId,
            source,
            exceptionType = exception.GetType().FullName,
            exception.Message,
            detail = exception.ToString(),
        }) + Environment.NewLine);
    }
}
