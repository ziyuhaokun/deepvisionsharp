using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class StorageCleanupService : IStorageCleanupService
{
    /// <summary>
    /// Newly created run directories are never treated as orphans. Covers the window between
    /// <c>CreateRunPlan</c> (directory created) and <c>MarkPreparingAsync</c> (row written).
    /// </summary>
    private static readonly TimeSpan RunDirectoryGracePeriod = TimeSpan.FromHours(2);

    private readonly IProjectFileStore _projectFileStore;
    private readonly ITrainingJobRepository _trainingJobs;
    private readonly IProjectRepository _projects;
    private readonly TrainingRunQueue _runQueue;
    private readonly ILogger<StorageCleanupService> _logger;

    public StorageCleanupService(
        IProjectFileStore projectFileStore,
        ITrainingJobRepository trainingJobs,
        IProjectRepository projects,
        TrainingRunQueue runQueue,
        ILogger<StorageCleanupService> logger)
    {
        _projectFileStore = projectFileStore;
        _trainingJobs = trainingJobs;
        _projects = projects;
        _runQueue = runQueue;
        _logger = logger;
    }

    public Task<Result<StorageCleanupResult>> CleanAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // All IO + enumeration on a thread-pool worker so Shell UI stays responsive.
        return Task.Run(
            () => CleanCoreAsync(progress, cancellationToken),
            cancellationToken);
    }

    private async Task<Result<StorageCleanupResult>> CleanCoreAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            progress?.Report("正在清理临时缓存目录…");
            var (tempEntries, tempBytes) = CleanTempCacheDirectories(progress, cancellationToken);

            progress?.Report("正在扫描未引用的训练输出目录…");
            var (orphanRuns, orphanBytes) = await CleanOrphanTrainingRunsAsync(progress, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(
                $"清理完成：临时 {tempEntries} 项 / 未引用训练目录 {orphanRuns} 个。");

            _logger.LogInformation(
                "Storage cleanup finished. TempEntries={TempEntries}, TempBytes={TempBytes}, OrphanRuns={OrphanRuns}, OrphanBytes={OrphanBytes}",
                tempEntries,
                tempBytes,
                orphanRuns,
                orphanBytes);

            return Result<StorageCleanupResult>.Success(
                new StorageCleanupResult(tempEntries, tempBytes, orphanRuns, orphanBytes));
        }
        catch (OperationCanceledException)
        {
            progress?.Report("清理已取消。");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage cleanup failed");
            progress?.Report($"清理失败：{ex.Message}");
            return Result<StorageCleanupResult>.Failure($"清理失败：{ex.Message}");
        }
    }

    private (int Entries, long Bytes) CleanTempCacheDirectories(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var dataRoot = _projectFileStore.DataDirectory;
        var targets = new[]
        {
            Path.Combine(dataRoot, "temp"),
            Path.Combine(dataRoot, "cache"),
            Path.Combine(dataRoot, "imports")
        };

        var deleted = 0;
        long bytes = 0;
        foreach (var target in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(target))
            {
                continue;
            }

            progress?.Report($"删除临时目录：{Path.GetFileName(target)}");
            bytes += GetDirectorySize(target);
            DeleteDirectorySafe(target);
            deleted++;
            _logger.LogInformation("Deleted temp/cache directory {Path}", target);
        }

        return (deleted, bytes);
    }

    private async Task<(int Runs, long Bytes)> CleanOrphanTrainingRunsAsync(
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var jobs = await _trainingJobs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var protectedRoots = CollectProtectedRunDirectories(jobs);

        // A run directory is created (TrainingWorkspacePlanner.CreateRunPlan) before the job row is
        // written (MarkPreparingAsync), so a sweep running in that window would see a fresh directory
        // that no row references yet. Protect recent directories and anything the run queue still
        // tracks, on top of the rows themselves.
        var recentCutoff = DateTime.UtcNow - RunDirectoryGracePeriod;

        var activeProjects = (await _projects.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(project => ProjectCapabilityCatalog.IsActive(project.Type))
            .Select(project => project.Id).ToHashSet();
        progress?.Report($"已加载 {jobs.Count} 个训练任务引用，开始扫描磁盘…");

        var projectsRoot = Path.Combine(_projectFileStore.DataDirectory, "projects");
        if (!Directory.Exists(projectsRoot))
        {
            return (0, 0);
        }

        var deletedRuns = 0;
        long reclaimed = 0;

        foreach (var projectDir in Directory.EnumerateDirectories(projectsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runsRoot = Path.Combine(projectDir, "runs");
            if (!Guid.TryParse(Path.GetFileName(projectDir), out var projectId) || !activeProjects.Contains(projectId))
            {
                continue;
            }
            if (!Directory.Exists(runsRoot))
            {
                continue;
            }

            // Layout: projects/{projectId}/runs/{workspace}/{runVersion}/
            foreach (var workspaceDir in Directory.EnumerateDirectories(runsRoot))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var runDir in Directory.EnumerateDirectories(workspaceDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string fullPath;
                    try
                    {
                        fullPath = Path.GetFullPath(runDir);
                    }
                    catch
                    {
                        continue;
                    }

                    if (protectedRoots.Contains(fullPath))
                    {
                        continue;
                    }

                    // Never delete a directory that is an ancestor of a protected path.
                    if (IsAncestorOfAny(fullPath, protectedRoots))
                    {
                        continue;
                    }

                    // A run that just started has a directory but no job row yet; its name ends with
                    // the job id, which the queue tracks from enqueue until the run finishes.
                    if (IsRecentlyCreatedOrTracked(runDir, recentCutoff))
                    {
                        continue;
                    }

                    var size = GetDirectorySize(fullPath);
                    if (!DeleteDirectorySafe(fullPath))
                    {
                        _logger.LogWarning("Failed to delete orphan training run {Path}", fullPath);
                        progress?.Report($"跳过无法删除：{Path.GetFileName(fullPath)}");
                        continue;
                    }

                    deletedRuns++;
                    reclaimed += size;
                    progress?.Report($"已删除未引用训练目录：{Path.GetFileName(fullPath)}");
                    _logger.LogInformation(
                        "Deleted orphan training run directory {Path} ({Bytes} bytes)",
                        fullPath,
                        size);
                }

                // Drop empty workspace folders left behind.
                TryDeleteEmptyDirectory(workspaceDir);
            }

            TryDeleteEmptyDirectory(runsRoot);
        }

        return (deletedRuns, reclaimed);
    }

    private static HashSet<string> CollectProtectedRunDirectories(IReadOnlyList<TrainingJob> jobs)
    {
        var protectedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in jobs)
        {
            // Active jobs always protect their output root (even if path temporarily missing).
            if (TrainingJobTransitions.IsActive(job.Status)
                && !string.IsNullOrWhiteSpace(job.OutputRoot))
            {
                TryAddDirectory(protectedRoots, job.OutputRoot);
            }

            if (!string.IsNullOrWhiteSpace(job.OutputRoot))
            {
                TryAddDirectory(protectedRoots, job.OutputRoot);
            }

            // Best weights / logs may sit under the run directory even if OutputRoot was rewritten.
            TryAddContainingDirectory(protectedRoots, job.BestModelPath);
            TryAddContainingDirectory(protectedRoots, job.LogFile);
        }

        return protectedRoots;
    }

    /// <summary>
    /// Run directory names end with the 32-char job id (<c>{name}_{timestamp}_{jobId:N}</c>).
    /// </summary>
    private bool IsRecentlyCreatedOrTracked(string runDirectory, DateTime recentCutoff)
    {
        var name = Path.GetFileName(runDirectory);
        var separator = name.LastIndexOf('_');
        if (separator >= 0 && separator + 1 < name.Length)
        {
            var idPart = name[(separator + 1)..];
            if (idPart.Length == 32 && Guid.TryParseExact(idPart, "N", out var jobId)
                && _runQueue.IsTracked(jobId))
            {
                return true;
            }
        }

        try
        {
            return Directory.GetCreationTimeUtc(runDirectory) >= recentCutoff;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable timestamp: leave the directory alone rather than risk deleting a live run.
            return true;
        }
    }

    private static void TryAddDirectory(HashSet<string> set, string path)
    {
        try
        {
            var full = Path.GetFullPath(path.Trim());
            set.Add(full);
        }
        catch
        {
            // ignore invalid paths
        }
    }

    private static void TryAddContainingDirectory(HashSet<string> set, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var full = Path.GetFullPath(filePath.Trim());
            // Prefer the run folder: weights live in .../run/train/weights/best.pt or .../run/best.pt
            // Walk up until we hit a parent named like a version folder under runs/{workspace}/.
            var directory = File.Exists(full)
                ? Path.GetDirectoryName(full)
                : Directory.Exists(full)
                    ? full
                    : Path.GetDirectoryName(full);

            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var runRoot = ResolveTrainingRunRoot(directory);
            if (!string.IsNullOrWhiteSpace(runRoot))
            {
                set.Add(runRoot);
            }
            else
            {
                set.Add(Path.GetFullPath(directory));
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// Resolve .../runs/{workspace}/{runVersion} from a path nested under a run.
    /// </summary>
    private static string? ResolveTrainingRunRoot(string directory)
    {
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(directory));
            while (current?.Parent is not null)
            {
                // .../runs/default/runVersion  → Parent.Parent.Name == "runs", Parent.Name == workspace
                if (current.Parent.Parent is { Name: var grandName }
                    && string.Equals(grandName, "runs", StringComparison.OrdinalIgnoreCase)
                    && current.Parent.Parent.Parent is not null)
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static bool IsAncestorOfAny(string candidate, HashSet<string> protectedRoots)
    {
        var prefix = candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        foreach (var protectedPath in protectedRoots)
        {
            if (protectedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return 0;
            }

            long total = 0;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // skip locked files
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    private static bool DeleteDirectorySafe(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                catch
                {
                    // best effort
                }
            }

            Directory.Delete(path, recursive: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path, recursive: false);
            }
        }
        catch
        {
            // ignore
        }
    }
}
