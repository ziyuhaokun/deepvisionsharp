using System.Globalization;
using System.IO;

using DeepVisionSharp.Presentation.Logging;

using Microsoft.Extensions.Logging;

namespace DeepVisionSharp.Shell.Avalonia.Services.Logging;

/// <summary>
/// 删除超过 <see cref="FileLoggerOptions.RetentionDays"/> 天的日志文件，并把整个日志目录
/// 压到 <see cref="FileLoggerOptions.MaxTotalBytes"/> 以内。启动跑一次，之后每 24 小时一次。
/// </summary>
/// <remarks>
/// <para>
/// 保留期按文件名里编码的日期（<c>yyyy-MM-dd.log</c>、<c>yyyy-MM-dd-exceptions.log</c>、
/// <c>yyyy-MM-dd-crash-latest.txt</c>）判断，不看文件时间戳：拷贝/还原目录不会让过期日志复活。
/// 文件名读不出日期的文件不动——要么与本应用无关，要么是人手工放进去的。
/// </para>
/// <para>
/// 生命周期由 <see cref="App"/> 驱动，而不是注册成 <c>IHostedService</c>：组合根
/// （<c>Composition/AppComposition.cs</c>）不在启动流程这一路的改动范围内，
/// 文件日志的提供程序与保留服务统一在 App 里挂到 Host 的 <c>ILoggerFactory</c> 上。
/// </para>
/// <para>
/// 与 WPF Shell 的 <c>DeepVisionSharp.Shell.Logging.LogRetentionService</c> 同一套清理规则，
/// 迁移期各持一份实现；收口到 Presentation 后这里删掉。
/// </para>
/// </remarks>
internal sealed class LogRetentionService : IDisposable
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

    private readonly FileLoggerOptions _options;
    private readonly ILogger<LogRetentionService> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public LogRetentionService(FileLoggerOptions options, ILogger<LogRetentionService> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>启动即清理，不阻塞调用线程（首屏启动不等磁盘扫描）。</summary>
    public void Start()
    {
        _loop ??= Task.Run(() => RunAsync(_stopping.Token));
    }

    /// <summary>停掉定时器并等后台任务收尾；退出路径调用，不能无限等。</summary>
    public void Stop(TimeSpan timeout)
    {
        try
        {
            _stopping.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (_loop is null)
        {
            return;
        }

        try
        {
            if (!_loop.Wait(timeout))
            {
                _logger.LogWarning("Log retention loop did not stop within {Timeout}", timeout);
            }
        }
        catch (AggregateException ex)
        {
            _logger.LogWarning(ex, "Log retention loop finished with an error");
        }
    }

    public void Dispose()
    {
        Stop(TimeSpan.FromSeconds(2));
        _stopping.Dispose();
    }

    private async Task RunAsync(CancellationToken stoppingToken)
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
            _logger.LogWarning(ex, "Startup log retention sweep failed");
        }

        using var timer = new PeriodicTimer(SweepInterval);
        while (true)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }

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
    /// 先删过期文件，再按最旧优先裁剪，直到目录大小符合预算。返回删除的文件数与回收的字节数。
    /// </summary>
    private (int Deleted, long Bytes) Sweep(DateTimeOffset now)
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
                if (TryDelete(file, out var expiredSize))
                {
                    deleted++;
                    bytes += expiredSize;
                }

                continue;
            }

            candidates.Add((file, fileDate));
        }

        // 体积预算：最旧的先删，保证最新的诊断信息留下来。
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
                if (TryDescribe(path) is { } info)
                {
                    yield return info;
                }
            }
        }

        // 崩溃报告放在日志根目录，不在 stage 子目录里。
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
            if (TryDescribe(path) is { } info)
            {
                yield return info;
            }
        }
    }

    private static FileInfo? TryDescribe(string path)
    {
        try
        {
            return new FileInfo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>读取文件名开头的 <c>yyyy-MM-dd</c>；没有这个前缀的文件名不参与保留期判断。</summary>
    private static bool TryReadDate(string fileName, out DateTime date)
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
            // 被别的进程（日志本身、编辑器）打开的文件下次扫描再删，不让整轮清理失败。
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
