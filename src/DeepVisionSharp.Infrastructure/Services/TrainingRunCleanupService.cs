using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Deletes a training run directory only when it is a direct child of
/// projects/{projectId}/runs/default. Rejects reparse points and out-of-tree paths.
/// </summary>
internal sealed class TrainingRunCleanupService : ITrainingRunCleanupService
{
    private readonly IProjectFileStore _projectFileStore;
    private readonly ILogger<TrainingRunCleanupService> _logger;
    private readonly SqliteDb _sqliteDb;

    public TrainingRunCleanupService(
        IProjectFileStore projectFileStore,
        SqliteDb sqliteDb,
        ILogger<TrainingRunCleanupService> logger)
    {
        _projectFileStore = projectFileStore;
        _sqliteDb = sqliteDb;
        _logger = logger;
    }

    public async Task<Result<TrainingRunCleanupResult>> DeleteRunAsync(
        Guid projectId,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_sqliteDb.Instance, projectId, cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => DeleteRunCore(projectId, outputRoot, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Training run cleanup cancelled for project {ProjectId}: {OutputRoot}",
                projectId,
                outputRoot);
            return Result<TrainingRunCleanupResult>.Failure(
                "清理训练运行目录已取消。若目录已部分删除，可再次删除任务以完成清理。");
        }
    }

    private Result<TrainingRunCleanupResult> DeleteRunCore(
        Guid projectId,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (projectId == Guid.Empty)
        {
            return Result<TrainingRunCleanupResult>.Failure("项目标识无效，无法清理训练运行目录。");
        }

        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return Result<TrainingRunCleanupResult>.Failure("训练运行目录路径为空，无法清理。");
        }

        string fullOutput;
        string fullRunsRoot;
        try
        {
            fullOutput = NormalizeDirectoryPath(Path.GetFullPath(outputRoot.Trim()));
            fullRunsRoot = NormalizeDirectoryPath(
                Path.GetFullPath(_projectFileStore.GetTrainingRunsRoot(projectId, "default")));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.LogWarning(
                ex,
                "Invalid training run path for project {ProjectId}: {OutputRoot}",
                projectId,
                outputRoot);
            return Result<TrainingRunCleanupResult>.Failure(
                "训练运行目录路径无效，已拒绝清理。请检查任务 OutputRoot 后重试。");
        }

        if (!Path.IsPathRooted(fullOutput))
        {
            return Result<TrainingRunCleanupResult>.Failure(
                "训练运行目录必须是绝对路径，已拒绝清理。");
        }

        // Only allow runs/default/{runName} — never the runs root itself or nested deeper paths.
        var parent = Directory.GetParent(fullOutput);
        if (parent is null)
        {
            _logger.LogWarning(
                "Rejected training run cleanup: no parent for {OutputRoot} (project {ProjectId})",
                fullOutput,
                projectId);
            return Result<TrainingRunCleanupResult>.Failure(
                "训练运行目录路径缺少父目录，已拒绝清理。");
        }

        string fullParent;
        try
        {
            fullParent = NormalizeDirectoryPath(Path.GetFullPath(parent.FullName));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Result<TrainingRunCleanupResult>.Failure(
                "训练运行目录父路径无效，已拒绝清理。");
        }

        if (!PathsEqual(fullParent, fullRunsRoot))
        {
            _logger.LogWarning(
                "Rejected training run cleanup outside runs/default: OutputRoot={OutputRoot}, ExpectedParent={ExpectedParent}, ActualParent={ActualParent}, ProjectId={ProjectId}",
                fullOutput,
                fullRunsRoot,
                fullParent,
                projectId);
            return Result<TrainingRunCleanupResult>.Failure(
                "训练运行目录不在当前项目的 runs/default 下，已拒绝清理以保护其他数据。"
                + $" 路径：{fullOutput}");
        }

        if (PathsEqual(fullOutput, fullRunsRoot))
        {
            _logger.LogWarning(
                "Rejected training run cleanup of runs root itself for project {ProjectId}",
                projectId);
            return Result<TrainingRunCleanupResult>.Failure(
                "不能删除训练运行根目录，已拒绝清理。");
        }

        var directoryName = Path.GetFileName(fullOutput);
        if (string.IsNullOrWhiteSpace(directoryName)
            || directoryName is "." or "..")
        {
            return Result<TrainingRunCleanupResult>.Failure(
                "训练运行目录名无效，已拒绝清理。");
        }

        if (!Directory.Exists(fullOutput))
        {
            _logger.LogInformation(
                "Training run directory already missing for project {ProjectId}: {Path}",
                projectId,
                fullOutput);
            return Result<TrainingRunCleanupResult>.Success(
                new TrainingRunCleanupResult(
                    DirectoryDeleted: false,
                    AlreadyMissing: true,
                    DeletedPath: fullOutput));
        }

        if (IsReparsePoint(fullOutput))
        {
            _logger.LogWarning(
                "Rejected training run cleanup of reparse point for project {ProjectId}: {Path}",
                projectId,
                fullOutput);
            return Result<TrainingRunCleanupResult>.Failure(
                "训练运行目录是符号链接或联接，已拒绝递归删除。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            ClearReadOnlyAttributes(fullOutput);
            Directory.Delete(fullOutput, recursive: true);

            _logger.LogInformation(
                "Deleted training run directory for project {ProjectId}: {Path}",
                projectId,
                fullOutput);

            return Result<TrainingRunCleanupResult>.Success(
                new TrainingRunCleanupResult(
                    DirectoryDeleted: true,
                    AlreadyMissing: false,
                    DeletedPath: fullOutput));
        }
        catch (DirectoryNotFoundException)
        {
            _logger.LogInformation(
                "Training run directory disappeared during cleanup for project {ProjectId}: {Path}",
                projectId,
                fullOutput);
            return Result<TrainingRunCleanupResult>.Success(
                new TrainingRunCleanupResult(
                    DirectoryDeleted: false,
                    AlreadyMissing: true,
                    DeletedPath: fullOutput));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(
                ex,
                "Unauthorized access deleting training run for project {ProjectId}: {Path}",
                projectId,
                fullOutput);
            return Result<TrainingRunCleanupResult>.Failure(
                $"没有权限删除训练运行目录：{fullOutput}");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(
                ex,
                "IO error deleting training run for project {ProjectId}: {Path}",
                projectId,
                fullOutput);
            return Result<TrainingRunCleanupResult>.Failure(
                $"无法删除训练运行目录（可能被占用）：{ex.Message}");
        }
    }

    /// <summary>
    /// Strip trailing separators so GetParent(runName\) returns runs/default, not runName itself.
    /// Safe for product paths under LocalAppData (never a drive root).
    /// </summary>
    private static string NormalizeDirectoryPath(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            NormalizeDirectoryPath(left),
            NormalizeDirectoryPath(right),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // If attributes cannot be read, refuse delete rather than risk following a link.
            return true;
        }
    }

    private void ClearReadOnlyAttributes(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort before Directory.Delete; hard failures surface there.
                _logger.LogDebug(ex, "Could not clear read-only attribute on {File}", file);
            }
        }
    }
}
