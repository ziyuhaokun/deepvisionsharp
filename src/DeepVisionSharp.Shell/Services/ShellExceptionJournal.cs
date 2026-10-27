using System.Text.Json;
using DeepVisionSharp.Shell.Logging;

namespace DeepVisionSharp.Shell.Services;

/// <summary>Machine-readable failure evidence, including handled Dispatcher/static reports.</summary>
public static class ShellExceptionJournal
{
    private static int _reportedCount;
    public static int ReportedCount => Volatile.Read(ref _reportedCount);

    internal static void Record(Exception exception, string source)
    {
        var paths = ShellRuntimePaths.Current;
        if (!paths.IsVerification) return;
        Interlocked.Increment(ref _reportedCount);
        // Also fail a graceful exit when the evidence disk is unavailable.
        Environment.ExitCode = 70;
        FileLoggerProvider.AppendToFile(paths.ExceptionJournalPath, JsonSerializer.Serialize(new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            processId = Environment.ProcessId,
            source,
            exceptionType = exception.GetType().FullName,
            exception.Message,
            detail = exception.ToString()
        }) + Environment.NewLine);
    }
}
