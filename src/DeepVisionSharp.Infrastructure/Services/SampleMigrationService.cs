using System.Data;
using System.Data.Common;
using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class SampleMigrationService : ISampleMigrationService
{
    private readonly IFreeSql _db;
    private readonly IProjectFileStore _fileStore;
    private readonly ILogger<SampleMigrationService> _logger;
    private readonly ProjectOperationGate? _operations;
    private readonly Func<CancellationToken, Task>? _beforeDatabaseMutation;

    public SampleMigrationService(
        SqliteDb sqliteDb,
        IProjectFileStore fileStore,
        ProjectOperationGate operations,
        ILogger<SampleMigrationService> logger)
        : this(sqliteDb.Instance, fileStore, logger, operations: operations)
    {
    }

    internal SampleMigrationService(
        IFreeSql db,
        IProjectFileStore fileStore,
        ILogger<SampleMigrationService> logger,
        ProjectOperationGate? operations = null,
        Func<CancellationToken, Task>? beforeDatabaseMutation = null)
    {
        _db = db;
        _fileStore = fileStore;
        _logger = logger;
        _operations = operations;
        _beforeDatabaseMutation = beforeDatabaseMutation;
    }

    public async Task<Result> MoveAsync(
        Guid sourceGalleryId,
        Guid targetGalleryId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var batchResult = await MoveManyAsync(
            sourceGalleryId,
            targetGalleryId,
            [fileName],
            new SampleMoveOptions(AutoRenameOnConflict: false),
            progress: null,
            cancellationToken);

        if (batchResult.IsFailure)
        {
            return Result.Failure(batchResult.Error ?? "迁移样本失败");
        }

        var result = batchResult.Value!;
        if (result.Failed > 0)
        {
            var error = result.Items.FirstOrDefault(item => !item.Succeeded)?.Error ?? "迁移样本失败";
            return Result.Failure(error);
        }

        return Result.Success();
    }

    public async Task<Result<SampleBatchMoveResult>> MoveManyAsync(
        Guid sourceGalleryId,
        Guid targetGalleryId,
        IReadOnlyList<string> fileNames,
        SampleMoveOptions options,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, sourceGalleryId, cancellationToken).ConfigureAwait(false);
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, targetGalleryId, cancellationToken).ConfigureAwait(false);
        if (fileNames.Count == 0)
        {
            return Result<SampleBatchMoveResult>.Failure("未选择任何样本。");
        }

        // Files are moved before the rows are updated, so a project deletion landing mid-migration
        // would leave the rollback writing files into a directory that no longer exists. Hold the
        // same gate deletion honours.
        IDisposable? operation = null;
        if (_operations is not null)
        {
            var owner = await _db.Select<SqlGallery>()
                .Where(item => item.Id == sourceGalleryId)
                .FirstAsync(item => item.ProjectId, cancellationToken)
                .ConfigureAwait(false);
            if (owner != Guid.Empty)
            {
                operation = _operations.EnterStart(owner);
            }
        }

        using var operationLease = operation;

        if (sourceGalleryId == targetGalleryId)
        {
            return Result<SampleBatchMoveResult>.Failure("源图库和目标图库不能相同");
        }

        var sourceGallery = await _db.Select<SqlGallery>()
            .Where(gallery => gallery.Id == sourceGalleryId)
            .FirstAsync(cancellationToken);
        if (sourceGallery is null)
        {
            return Result<SampleBatchMoveResult>.Failure("源图库不存在");
        }

        var targetGallery = await _db.Select<SqlGallery>()
            .Where(gallery => gallery.Id == targetGalleryId)
            .FirstAsync(cancellationToken);
        if (targetGallery is null)
        {
            return Result<SampleBatchMoveResult>.Failure("目标图库不存在");
        }

        if (sourceGallery.ProjectId != targetGallery.ProjectId)
        {
            return Result<SampleBatchMoveResult>.Failure("仅支持在同一项目内迁移样本");
        }

        // Prepare selected names first so we only load matching source rows (not the whole gallery).
        var distinctFileNames = fileNames
            .Select(Path.GetFileName)
            .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sourceSamplesByFileName = await LoadSourceSamplesByFileNamesAsync(
            sourceGallery.Id,
            distinctFileNames!,
            cancellationToken);

        // Target only needs occupied file names for conflict / auto-rename (not full SqlSample rows).
        var reservedNames = await LoadTargetReservedFileNamesAsync(targetGallery.Id, cancellationToken);

        // Batch-load annotations for selected source files (avoids N+1 FirstAsync per sample).
        var sourceAnnotationsByFileName = await LoadSourceAnnotationsByFileNamesAsync(
            sourceGallery.Id,
            distinctFileNames!,
            cancellationToken);

        var targetImageDirectory = _fileStore.GetGalleryImagesDirectory(
            targetGallery.ProjectId,
            targetGallery.Id,
            ensureExists: true);

        var pendingMoves = new List<PendingSampleMove>();
        var itemResults = new List<SampleMoveItemResult>();

        for (var index = 0; index < distinctFileNames.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safeFileName = distinctFileNames[index]!;
            progress?.Report(new GalleryTransferProgress(
                "准备迁移样本",
                index,
                distinctFileNames.Length,
                0,
                safeFileName));

            if (!sourceSamplesByFileName.TryGetValue(safeFileName, out var sourceSample))
            {
                itemResults.Add(new SampleMoveItemResult(safeFileName, null, false, $"源图库中不存在样本：{safeFileName}"));
                continue;
            }

            var targetFileName = options.AutoRenameOnConflict
                ? SampleFileNameConflictResolver.ResolveUniqueFileName(safeFileName, targetImageDirectory, reservedNames)
                : safeFileName;

            if (!options.AutoRenameOnConflict
                && (reservedNames.Contains(safeFileName)
                    || File.Exists(Path.Combine(targetImageDirectory, safeFileName))))
            {
                itemResults.Add(new SampleMoveItemResult(safeFileName, null, false, $"目标图库已存在同名文件：{safeFileName}"));
                continue;
            }

            var sourceImagePath = _fileStore.ResolveSampleImagePath(
                sourceGallery.ProjectId,
                sourceGallery.Id,
                safeFileName);
            if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
            {
                itemResults.Add(new SampleMoveItemResult(safeFileName, null, false, $"源图像文件不存在：{safeFileName}"));
                continue;
            }

            var targetImagePath = Path.Combine(targetImageDirectory, targetFileName);
            if (!options.AutoRenameOnConflict && File.Exists(targetImagePath))
            {
                itemResults.Add(new SampleMoveItemResult(safeFileName, null, false, $"目标图库图像文件已存在：{safeFileName}"));
                continue;
            }

            // A sidecar without its image (a leftover from an earlier failed import) would make the
            // move fail with FileMode.CreateNew, which is caught outside this loop and rolls back the
            // whole batch. Report the sample as failed instead and let the rest proceed.
            if (!options.AutoRenameOnConflict && File.Exists(SampleAnnotationFiles.GetPath(targetImagePath)))
            {
                itemResults.Add(new SampleMoveItemResult(
                    safeFileName,
                    null,
                    false,
                    $"目标图库已存在同名标注文件：{SampleAnnotationFiles.GetPath(targetImagePath)}"));
                continue;
            }

            sourceAnnotationsByFileName.TryGetValue(safeFileName, out var sourceAnnotation);

            // Reading a sample's annotation can fail on a corrupt sidecar or an IO error, and
            // PreserveLegacyCollisionAsync writes to disk. Neither is inside the move/rollback scope
            // below, so an exception here would escape the Result contract entirely; record the
            // sample as failed and keep the rest of the batch going instead.
            SampleAnnotationFiles.Content content;
            try
            {
                await SampleAnnotationFiles
                    .PreserveLegacyCollisionAsync(_db, sourceGallery.Id, sourceImagePath, cancellationToken)
                    .ConfigureAwait(false);
                content = await SampleAnnotationFiles
                    .ReadAsync(sourceImagePath, sourceAnnotation?.DocumentJson, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cannot read annotation for sample {FileName}", safeFileName);
                itemResults.Add(new SampleMoveItemResult(
                    safeFileName,
                    null,
                    false,
                    $"读取标注失败：{ex.Message}"));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(content.Json))
            {
                sourceAnnotation ??= new SqlAnnotation { FileName = safeFileName, GalleryId = sourceGallery.Id };
                sourceAnnotation.DocumentJson = content.Json;
            }

            reservedNames.Add(targetFileName);
            pendingMoves.Add(new PendingSampleMove
            {
                SourceSample = sourceSample,
                SourceFileName = safeFileName,
                TargetFileName = targetFileName,
                SourceAnnotation = sourceAnnotation,
                AnnotationContent = content,
                SourceImagePath = sourceImagePath,
                TargetImagePath = targetImagePath
            });
        }

        if (pendingMoves.Count == 0)
        {
            var failedOnly = new SampleBatchMoveResult(
                false,
                distinctFileNames.Length,
                0,
                0,
                itemResults.Count(item => !item.Succeeded),
                itemResults);
            return Result<SampleBatchMoveResult>.Success(failedOnly);
        }

        var allMovedFiles = new List<MovedFile>();
        var completedMoves = new List<PendingSampleMove>();
        try
        {
            for (var index = 0; index < pendingMoves.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var pending = pendingMoves[index];
                progress?.Report(new GalleryTransferProgress(
                    "迁移样本",
                    index,
                    pendingMoves.Count,
                    completedMoves.Count,
                    pending.SourceFileName));

                // Track every successful operation immediately, including the current partially moved sample.
                MoveFile(pending.SourceImagePath, pending.TargetImagePath, allMovedFiles, cancellationToken);

                var targetAnnotationPath = SampleAnnotationFiles.GetPath(pending.TargetImagePath);
                if (pending.AnnotationContent.FilePath is { } sourceAnnotationPath)
                {
                    MoveFile(sourceAnnotationPath, targetAnnotationPath, allMovedFiles, cancellationToken);
                }
                else if (!string.IsNullOrWhiteSpace(pending.AnnotationContent.Json))
                {
                    // CreateNew preserves an unrelated orphan sidecar; rollback also covers partial writes.
                    await using var stream = new FileStream(targetAnnotationPath, FileMode.CreateNew, FileAccess.Write);
                    allMovedFiles.Add(new MovedFile(null, targetAnnotationPath));
                    await using var writer = new StreamWriter(stream);
                    await writer.WriteAsync(pending.AnnotationContent.Json.AsMemory(), cancellationToken);
                }

                completedMoves.Add(pending);
            }

            if (_beforeDatabaseMutation is not null)
            {
                await _beforeDatabaseMutation(cancellationToken);
            }

            await MoveDatabaseRowsBatchAsync(
                sourceGallery,
                targetGallery,
                completedMoves,
                cancellationToken);

            var moved = 0;
            var renamed = 0;
            foreach (var pending in completedMoves)
            {
                moved++;
                if (!string.Equals(pending.SourceFileName, pending.TargetFileName, StringComparison.OrdinalIgnoreCase))
                {
                    renamed++;
                }

                itemResults.Add(new SampleMoveItemResult(
                    pending.SourceFileName,
                    pending.TargetFileName,
                    true,
                    null));
            }

            var failed = itemResults.Count(item => !item.Succeeded);
            var batchResult = new SampleBatchMoveResult(
                failed == 0,
                distinctFileNames.Length,
                moved,
                renamed,
                failed,
                itemResults);

            _logger.LogInformation(
                "Moved {MovedCount} samples from gallery {SourceGalleryId} to {TargetGalleryId}",
                moved,
                sourceGallery.Id,
                targetGallery.Id);

            return Result<SampleBatchMoveResult>.Success(batchResult);
        }
        catch (Exception ex)
        {
            var rollbackFailed = RollbackMovedFiles(allMovedFiles);
            if (ex is OperationCanceledException)
            {
                throw;
            }

            _logger.LogWarning(
                ex,
                "Failed to move samples from gallery {SourceGalleryId} to {TargetGalleryId}",
                sourceGallery.Id,
                targetGallery.Id);

            var error = $"迁移样本失败：{ex.Message}";
            if (rollbackFailed)
            {
                error += "；文件回滚失败，请手动检查源图库和目标图库文件。";
            }

            return Result<SampleBatchMoveResult>.Failure(error);
        }
    }

    /// <summary>
    /// Loads only the selected source samples (chunked IN queries), keyed by file name.
    /// </summary>
    private async Task<Dictionary<string, SqlSample>> LoadSourceSamplesByFileNamesAsync(
        Guid sourceGalleryId,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SqlSample>(StringComparer.OrdinalIgnoreCase);
        if (fileNames.Count == 0)
        {
            return result;
        }

        foreach (var chunk in Chunk(fileNames, SqlInClauseChunkSize))
        {
            // FreeSql/SQLite cannot translate local-array .Contains(string); use parameterized IN.
            var rows = await _db.Select<SqlSample>()
                .Where(sample => sample.GalleryId == sourceGalleryId)
                .Where("FileName in @fileNames", new { fileNames = chunk })
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                // Prefer first row if duplicates exist under different casing (should not happen).
                result.TryAdd(row.FileName, row);
            }
        }

        return result;
    }

    /// <summary>
    /// Loads only target file names needed for conflict detection / auto-rename.
    /// Avoids materializing full <see cref="SqlSample"/> rows for the entire target gallery.
    /// </summary>
    private async Task<HashSet<string>> LoadTargetReservedFileNamesAsync(
        Guid targetGalleryId,
        CancellationToken cancellationToken)
    {
        var fileNames = await _db.Select<SqlSample>()
            .Where(sample => sample.GalleryId == targetGalleryId)
            .ToListAsync(sample => sample.FileName, cancellationToken);

        return new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Batch-loads source annotations for selected file names (chunked IN queries).
    /// </summary>
    private async Task<Dictionary<string, SqlAnnotation>> LoadSourceAnnotationsByFileNamesAsync(
        Guid sourceGalleryId,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, SqlAnnotation>(StringComparer.OrdinalIgnoreCase);
        if (fileNames.Count == 0)
        {
            return result;
        }

        foreach (var chunk in Chunk(fileNames, SqlInClauseChunkSize))
        {
            // FreeSql/SQLite cannot translate local-array .Contains(string); use parameterized IN.
            var rows = await _db.Select<SqlAnnotation>()
                .Where(annotation => annotation.GalleryId == sourceGalleryId)
                .Where("FileName in @fileNames", new { fileNames = chunk })
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                result.TryAdd(row.FileName, row);
            }
        }

        return result;
    }

    private static IEnumerable<string[]> Chunk(IReadOnlyList<string> items, int size)
    {
        if (items.Count == 0)
        {
            yield break;
        }

        for (var offset = 0; offset < items.Count; offset += size)
        {
            var length = Math.Min(size, items.Count - offset);
            var chunk = new string[length];
            for (var i = 0; i < length; i++)
            {
                chunk[i] = items[offset + i];
            }

            yield return chunk;
        }
    }

    private const int SqlInClauseChunkSize = 400;

    private async Task MoveDatabaseRowsBatchAsync(
        SqlGallery sourceGallery,
        SqlGallery targetGallery,
        IReadOnlyList<PendingSampleMove> moves,
        CancellationToken cancellationToken)
    {
        using var connectionLease = _db.Ado.MasterPool.Get();
        var connection = connectionLease.Value;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var move in moves)
            {
                var affected = await _db.Update<SqlSample>()
                    .WithTransaction(transaction)
                    .Set(sample => sample.GalleryId, targetGallery.Id)
                    .Set(sample => sample.FileName, move.TargetFileName)
                    .Set(sample => sample.IsAnnotated, move.SourceSample.IsAnnotated)
                    .Set(sample => sample.HasError, move.SourceSample.HasError)
                    .Set(sample => sample.ErrorMessage, move.SourceSample.ErrorMessage)
                    .Where(sample => sample.Id == move.SourceSample.Id)
                    .ExecuteAffrowsAsync(cancellationToken);
                if (affected != 1)
                {
                    throw new InvalidOperationException($"样本数据库记录更新失败：{move.SourceFileName}");
                }

                await _db.Delete<SqlAnnotation>()
                    .WithTransaction(transaction)
                    .Where(annotation =>
                        annotation.GalleryId == targetGallery.Id
                        && annotation.FileName == move.TargetFileName)
                    .ExecuteAffrowsAsync(cancellationToken);

                await _db.Delete<SqlAnnotation>()
                    .WithTransaction(transaction)
                    .Where(annotation =>
                        annotation.GalleryId == sourceGallery.Id
                        && annotation.FileName == move.SourceFileName)
                    .ExecuteAffrowsAsync(cancellationToken);

                if (move.SourceAnnotation is not null)
                {
                    await _db.Insert(new SqlAnnotation
                    {
                        GalleryId = targetGallery.Id,
                        FileName = move.TargetFileName,
                        Width = move.SourceAnnotation.Width,
                        Height = move.SourceAnnotation.Height,
                        ImageData = move.SourceAnnotation.ImageData,
                        DocumentJson = move.SourceAnnotation.DocumentJson
                    })
                        .WithTransaction(transaction)
                        .ExecuteAffrowsAsync(cancellationToken);
                }

                await GallerySampleLabelIndexMaintainer.MoveSampleAsync(
                    _db,
                    sourceGallery.Id,
                    targetGallery.Id,
                    move.SourceFileName,
                    move.TargetFileName,
                    move.SourceAnnotation?.DocumentJson,
                    cancellationToken,
                    transaction);
            }

            var now = DateTime.UtcNow;
            await UpdateGalleryCountersAsync(sourceGallery.Id, now, transaction, cancellationToken);
            await UpdateGalleryCountersAsync(targetGallery.Id, now, transaction, cancellationToken);
            await RefreshProjectStatisticsAsync(sourceGallery.ProjectId, now, transaction, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to roll back sample migration database transaction");
            }

            throw;
        }
    }

    private async Task UpdateGalleryCountersAsync(
        Guid galleryId,
        DateTime now,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var sampleCount = (int)await _db.Select<SqlSample>()
            .WithTransaction(transaction)
            .Where(sample => sample.GalleryId == galleryId)
            .CountAsync(cancellationToken);
        var annotatedCount = (int)await _db.Select<SqlSample>()
            .WithTransaction(transaction)
            .Where(sample => sample.GalleryId == galleryId && sample.IsAnnotated)
            .CountAsync(cancellationToken);

        await _db.Update<SqlGallery>()
            .WithTransaction(transaction)
            .Set(gallery => gallery.SampleCount, sampleCount)
            .Set(gallery => gallery.AnnotatedCount, annotatedCount)
            .Set(gallery => gallery.LastUpdatedUtc, now)
            .Where(gallery => gallery.Id == galleryId)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    private async Task RefreshProjectStatisticsAsync(
        Guid projectId,
        DateTime now,
        DbTransaction transaction,
        CancellationToken cancellationToken)
    {
        var galleries = await _db.Select<SqlGallery>()
            .WithTransaction(transaction)
            .Where(gallery => gallery.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        var totalSamples = galleries.Sum(gallery => gallery.SampleCount);
        var totalAnnotated = galleries.Sum(gallery => gallery.AnnotatedCount);
        var progress = totalSamples > 0 ? (double)totalAnnotated / totalSamples : 0;

        await _db.Update<SqlProject>()
            .WithTransaction(transaction)
            .Set(project => project.SampleCount, totalSamples)
            .Set(project => project.Progress, progress)
            .Set(project => project.LastUpdatedUtc, now)
            .Where(project => project.Id == projectId)
            .ExecuteAffrowsAsync(cancellationToken);
    }

    private static void MoveFile(
        string sourcePath,
        string targetPath,
        List<MovedFile> movedFiles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.Move(sourcePath, targetPath);
        movedFiles.Add(new MovedFile(sourcePath, targetPath));
    }

    private bool RollbackMovedFiles(IReadOnlyList<MovedFile> movedFiles)
    {
        var failed = false;
        for (var index = movedFiles.Count - 1; index >= 0; index--)
        {
            var movedFile = movedFiles[index];
            try
            {
                if (movedFile.SourcePath is null)
                {
                    File.Delete(movedFile.TargetPath);
                    continue;
                }
                if (!File.Exists(movedFile.TargetPath))
                {
                    failed = true;
                    _logger.LogWarning("Cannot roll back moved file because target is missing: {TargetPath}", movedFile.TargetPath);
                    continue;
                }

                if (File.Exists(movedFile.SourcePath))
                {
                    failed = true;
                    _logger.LogWarning("Cannot roll back moved file because source already exists: {SourcePath}", movedFile.SourcePath);
                    continue;
                }

                var sourceDirectory = Path.GetDirectoryName(movedFile.SourcePath);
                if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
                {
                    // The source gallery directory is gone: either the whole project was deleted, or
                    // someone removed the gallery. Recreating it would "roll back" files into a
                    // project that no longer exists, leaving orphans nothing ever reads.
                    failed = true;
                    _logger.LogWarning(
                        "Cannot roll back moved file because its source directory is missing: {SourcePath}",
                        movedFile.SourcePath);
                    continue;
                }

                File.Move(movedFile.TargetPath, movedFile.SourcePath);
            }
            catch (Exception ex)
            {
                failed = true;
                _logger.LogError(
                    ex,
                    "Failed to roll back moved file {TargetPath} to {SourcePath}",
                    movedFile.TargetPath,
                    movedFile.SourcePath);
            }
        }

        return failed;
    }

    private sealed class PendingSampleMove
    {
        public required SqlSample SourceSample { get; init; }

        public required string SourceFileName { get; init; }

        public required string TargetFileName { get; init; }

        public SqlAnnotation? SourceAnnotation { get; init; }

        public required string SourceImagePath { get; init; }

        public required string TargetImagePath { get; init; }

        public required SampleAnnotationFiles.Content AnnotationContent { get; init; }
    }

    private readonly record struct MovedFile(string? SourcePath, string TargetPath);
}
