using System.Globalization;
using System.IO;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Logging;

/// <summary>
/// Deletes log files older than <see cref="FileLoggerOptions.RetentionDays"/> and bounds the
/// total log directory size. Runs once at startup and then once a day.
/// </summary>
/// <remarks>
/// Retention is driven by the date encoded in each file name (<c>yyyy-MM-dd.log</c>,
/// <c>yyyy-MM-dd-exceptions.log</c>, <c>yyyy-MM-dd-crash-latest.txt</c>) rather than by the file
/// timestamp, so copying or restoring a folder cannot resurrect expired logs. Files whose name does
/// not carry a parsable date are left alone: they are either unrelated or hand-placed.
/// </remarks>
public sealed class LogRetentionService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private readonly FileLoggerOptions _options;
    private readonly ILogger<LogRetentionService> _logger;

    public LogRetentionService(FileLoggerOptions options, ILogger<LogRetentionService> logger)
    {
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Startup sweep runs off the UI thread and never blocks host startup.
            await Task.Run(() => Sweep(DateTimeOffset.Now), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Startup log retention sweep failed");
        }

        using var timer = new PeriodicTimer(SweepInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await Task.Run(() => Sweep(DateTimeOffset.Now), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic log retention sweep failed");
            }
        }
    }

    /// <summary>
    /// Deletes expired logs and then trims the oldest remaining files until the directory fits the
    /// size budget. Returns the number of files removed and bytes reclaimed.
    /// </summary>
    internal (int Deleted, long Bytes) Sweep(DateTimeOffset now)
    {
        var deleted = 0;
        long bytes = 0;

        if (_options.RetentionDays <= 0 || string.IsNullOrWhiteSpace(_options.LogDirectory))
        {
            return (0, 0);
        }

        var root = _options.LogDirectory;
        if (!Directory.Exists(root))
        {
            return (0, 0);
        }

        var cutoff = now.Date.AddDays(-_options.RetentionDays);
        var candidates = new List<(FileInfo File, DateTime Date)>();
        foreach (var file in EnumerateLogFiles(root))
        {
            if (!TryReadDate(file.Name, out var fileDate))
            {
                continue;
            }

            if (fileDate < cutoff)
            {
                if (TryDelete(file, out var size))
                {
                    deleted++;
                    bytes += size;
                }

                continue;
            }

            candidates.Add((file, fileDate));
        }

        // Size budget: oldest first, so the newest diagnostics always survive.
        var totalBytes = candidates.Sum(item => SafeLength(item.File));
        foreach (var (file, _) in candidates.OrderBy(item => item.Date).ThenBy(item => item.File.Name, StringComparer.Ordinal))
        {
            if (totalBytes <= _options.MaxTotalBytes)
            {
                break;
            }

            var size = SafeLength(file);
            if (!TryDelete(file, out _))
            {
                continue;
            }

            totalBytes -= size;
            deleted++;
            bytes += size;
        }

        if (deleted > 0)
        {
            _logger.LogInformation(
                "Log retention removed {FileCount} file(s), reclaimed {Bytes} bytes (retentionDays={Days}, maxTotalBytes={MaxBytes})",
                deleted,
                bytes,
                _options.RetentionDays,
                _options.MaxTotalBytes);
        }

        RemoveEmptyStageDirectories(root);
        return (deleted, bytes);
    }

    private static IEnumerable<FileInfo> EnumerateLogFiles(string root)
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var directory in directories)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var path in files)
            {
                FileInfo info;
                try
                {
                    info = new FileInfo(path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                yield return info;
            }
        }

        // Crash reports sit at the log root, not under a stage folder.
        IEnumerable<string> rootFiles;
        try
        {
            rootFiles = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var path in rootFiles)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            yield return info;
        }
    }

    /// <summary>
    /// Reads the leading <c>yyyy-MM-dd</c> from a log file name. Names without it are ignored.
    /// </summary>
    internal static bool TryReadDate(string fileName, out DateTime date)
    {
        date = default;
        if (fileName.Length < 10)
        {
            return false;
        }

        return DateTime.TryParseExact(
            fileName[..10],
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Exists ? file.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private bool TryDelete(FileInfo file, out long size)
    {
        size = SafeLength(file);
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file held open by another process (the logger itself, or a text editor) is retried
            // on the next sweep instead of failing the whole run.
            _logger.LogDebug(ex, "Could not delete log file {Path}", file.FullName);
            return false;
        }
    }

    private static void RemoveEmptyStageDirectories(string root)
    {
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var directory in directories)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort only.
            }
        }
    }
}
