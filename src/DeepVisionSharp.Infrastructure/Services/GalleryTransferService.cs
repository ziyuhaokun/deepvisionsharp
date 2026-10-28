using System.Data;
using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services.Transfer;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class GalleryTransferService : IGalleryTransferService
{
    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif"];
    private readonly IFreeSql _db;
    private readonly IGalleryRepository _galleries;
    private readonly IProjectRepository _projects;
    private readonly IProjectFileStore _fileStore;
    private readonly ProjectOperationGate _operations;
    private readonly ILogger<GalleryTransferService> _logger;

    public GalleryTransferService(
        SqliteDb sqliteDb,
        IGalleryRepository galleries,
        IProjectRepository projects,
        IProjectFileStore fileStore,
        ProjectOperationGate operations,
        ILogger<GalleryTransferService> logger)
    {
        _db = sqliteDb.Instance;
        _galleries = galleries;
        _projects = projects;
        _fileStore = fileStore;
        _operations = operations;
        _logger = logger;
    }

    public async Task<GalleryTransferResult> ImportSamplesFromFilesAsync(
        Guid galleryId,
        IReadOnlyList<string> filePaths,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);

        // Take the gate before reading anything the import relies on. Reading the gallery first and
        // then acquiring the lease leaves a window in which the project can be deleted: the read
        // succeeds, the delete completes, and the lease is then granted on an empty state.
        var ownerProjectId = await _db.Select<SqlGallery>()
            .Where(item => item.Id == galleryId)
            .FirstAsync(item => item.ProjectId, cancellationToken)
            .ConfigureAwait(false);
        if (ownerProjectId == Guid.Empty)
        {
            return Failed("图库不存在");
        }

        // Long import: keep the project undeletable until the last sample is committed.
        if (!TryEnterProjectOperation(ownerProjectId, out var lease, out var busyError))
        {
            return Failed(busyError!);
        }

        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false);
        if (gallery is null)
        {
            lease?.Dispose();
            return Failed("图库不存在");
        }

        using var operation = lease;
        var imageFiles = filePaths
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(File.Exists)
            .ToArray();

        var jsonLookup = filePaths
            .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
            .Where(File.Exists)
            .GroupBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return await ImportImagesAsync(galleryId, imageFiles, jsonLookup, "正在导入图像", progress, cancellationToken);
    }

    public async Task<GalleryTransferResult> ImportSamplesFromFolderAsync(
        Guid galleryId,
        string folderPath,
        bool recursive = false,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        if (!Directory.Exists(folderPath))
        {
            var result = new GalleryTransferResult(false, 0, 0, 0, 0);
            result.Errors.Add($"目录不存在: {folderPath}");
            return result;
        }

        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false);
        if (gallery is null)
        {
            return Failed("图库不存在");
        }

        if (!TryEnterProjectOperation(gallery.ProjectId, out var lease, out var busyError))
        {
            return Failed(busyError!);
        }

        using var operation = lease;

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var imageFiles = Directory
            .GetFiles(folderPath, "*.*", searchOption)
            .Where(path => ImageExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var jsonLookup = Directory
            .GetFiles(folderPath, "*.json", searchOption)
            .GroupBy(Path.GetFullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return await ImportImagesAsync(galleryId, imageFiles, jsonLookup, "正在导入目录", progress, cancellationToken);
    }

    public async Task<GalleryTransferResult> ExportGalleryAsync(
        Guid galleryId,
        string outputFilePath,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken);
        if (gallery is null)
        {
            var missing = new GalleryTransferResult(false, 0, 0, 1, 0);
            missing.Errors.Add("图库不存在");
            return missing;
        }

        var outputDirectory = Path.GetDirectoryName(outputFilePath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        if (File.Exists(outputFilePath))
        {
            File.Delete(outputFilePath);
        }

        using var db = BuildSqlite(outputFilePath, autoSync: true);
        var galleryRow = new SqlGallery
        {
            Id = gallery.Id,
            ProjectId = gallery.ProjectId,
            Name = gallery.Name,
            Description = gallery.Description,
            SampleCount = gallery.SampleCount,
            AnnotatedCount = gallery.AnnotatedCount,
            LastUpdatedUtc = gallery.LastUpdatedAt.UtcDateTime
        };
        await db.Insert(galleryRow).ExecuteAffrowsAsync(cancellationToken);

        var imagesDirectory = _fileStore.GetGalleryImagesDirectory(gallery.ProjectId, gallery.Id, ensureExists: false);
        var exported = 0;
        var failed = 0;
        var skipped = 0;
        var result = new GalleryTransferResult(true, gallery.SampleCount, 0, 0, 0);

        const int pageSize = 200;
        var skip = 0;
        var processed = 0;

        while (true)
        {
            var page = await _galleries.ListSamplesAsync(galleryId, skip, pageSize, cancellationToken);
            if (page.Items.Count == 0)
            {
                break;
            }

            for (var index = 0; index < page.Items.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = page.Items[index];

                try
                {
                    await db.Insert(new SqlSample
                    {
                        Id = sample.Id,
                        GalleryId = gallery.Id,
                        FileName = sample.FileName,
                        IsAnnotated = sample.IsAnnotated,
                        HasError = sample.HasError,
                        ErrorMessage = sample.ErrorMessage
                    }).ExecuteAffrowsAsync(cancellationToken);

                    var imagePath = _fileStore.ResolveSampleImagePath(gallery.ProjectId, gallery.Id, sample.FileName)
                        ?? Path.Combine(imagesDirectory, sample.FileName);
                    string? imageData = null;
                    if (File.Exists(imagePath))
                    {
                        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                        imageData = Convert.ToBase64String(bytes);
                    }
                    else
                    {
                        skipped++;
                        result.Errors.Add($"缺失图像: {sample.FileName}");
                    }

                    var stored = await _db.Select<SqlAnnotation>()
                        .Where(row => row.GalleryId == gallery.Id && row.FileName == sample.FileName)
                        .FirstAsync(cancellationToken);
                    var documentJson = (await SampleAnnotationFiles.ReadAsync(imagePath, stored?.DocumentJson, cancellationToken)).Json ?? "{}";

                    await db.Insert(new SqlAnnotation
                    {
                        GalleryId = gallery.Id,
                        FileName = sample.FileName,
                        ImageData = imageData,
                        DocumentJson = documentJson
                    }).ExecuteAffrowsAsync(cancellationToken);

                    exported++;
                }
                catch (Exception ex)
                {
                    failed++;
                    result.Errors.Add($"{sample.FileName}: {ex.Message}");
                    _logger.LogError(ex, "Failed to export sample {Sample}", sample.FileName);
                }
                finally
                {
                    progress?.Report(new GalleryTransferProgress(
                        "正在导出图库",
                        processed + index + 1,
                        page.TotalCount,
                        exported,
                        sample.FileName));
                }
            }

            processed += page.Items.Count;
            if (processed >= page.TotalCount)
            {
                break;
            }

            skip += pageSize;
        }

        var exportResult = new GalleryTransferResult(failed == 0, gallery.SampleCount, exported, failed, skipped);
        exportResult.Errors.AddRange(result.Errors);
        return exportResult;
    }

    public async Task<GalleryTransferResult> ImportGalleryAsync(
        Guid projectId,
        string inputFilePath,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(inputFilePath))
        {
            var missing = new GalleryTransferResult(false, 0, 0, 1, 0);
            missing.Errors.Add("导入文件不存在");
            return missing;
        }

        if (!TryEnterProjectOperation(projectId, out var lease, out var busyError))
        {
            return Failed(busyError!);
        }

        using var operation = lease;

        using var db = BuildSqlite(inputFilePath, autoSync: false);
        var sourceGalleries = await db.Select<SqlGallery>().ToListAsync(cancellationToken);
        if (sourceGalleries.Count == 0)
        {
            var empty = new GalleryTransferResult(false, 0, 0, 0, 0);
            empty.Errors.Add("未找到可导入的图库数据");
            return empty;
        }

        var existingNames = new HashSet<string>(
            (await _galleries.ListByProjectAsync(projectId, cancellationToken)).Select(gallery => gallery.Name),
            StringComparer.OrdinalIgnoreCase);

        var total = sourceGalleries.Sum(gallery => (int)db.Select<SqlSample>().Where(sample => sample.GalleryId == gallery.Id).Count());
        var processed = 0;
        var added = 0;
        var failed = 0;
        var skipped = 0;
        var errors = new List<string>();
        var createdGalleries = new List<Guid>();

        try
        {
            foreach (var sourceGallery in sourceGalleries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var galleryName = TransferFileNames.MakeUniqueName(sourceGallery.Name, existingNames, "导入图库");
                var gallery = await _galleries.CreateAsync(
                    projectId,
                    galleryName,
                    sourceGallery.Description,
                    cancellationToken);
                createdGalleries.Add(gallery.Id);
                var imagesDirectory = _fileStore.GetGalleryImagesDirectory(projectId, gallery.Id, ensureExists: true);
                var sourceSamples = await db.Select<SqlSample>().Where(sample => sample.GalleryId == sourceGallery.Id).ToListAsync(cancellationToken);

                foreach (var sourceSample in sourceSamples)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Read one row at a time: materializing the gallery's annotations holds every
                        // Base64 image in memory (a page of 200 rows still costs hundreds of MB).
                        var annotation = await TransferAnnotationReader
                            .FindAsync(db, sourceGallery.Id, sourceSample.FileName, cancellationToken)
                            .ConfigureAwait(false);
                        if (annotation is null || string.IsNullOrWhiteSpace(annotation.ImageData))
                        {
                            skipped++;
                            errors.Add($"缺失图像: {sourceSample.FileName}");
                            continue;
                        }

                        if (!TransferFileNames.TryGetSafeFileName(sourceSample.FileName, out var safeFileName))
                        {
                            failed++;
                            errors.Add($"非法文件名: {sourceSample.FileName}");
                            continue;
                        }

                        var targetFileName = TransferFileNames.GenerateUniqueFileName(imagesDirectory, safeFileName);
                        var bytes = Convert.FromBase64String(TransferPayloads.ExtractBase64(annotation.ImageData)!);
                        var hasDocument = !string.IsNullOrWhiteSpace(annotation.DocumentJson) && annotation.DocumentJson != "{}";
                        await ImportSampleAsync(
                            gallery,
                            targetFileName,
                            (stream, token) => stream.WriteAsync(bytes, token).AsTask(),
                            hasDocument ? annotation.DocumentJson : null,
                            sourceSample.IsAnnotated || hasDocument,
                            sourceSample.HasError,
                            sourceSample.ErrorMessage,
                            cancellationToken);
                        added++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"{sourceSample.FileName}: {ex.Message}");
                        _logger.LogError(ex, "Failed to import sample {Sample}", sourceSample.FileName);
                    }
                    finally
                    {
                        processed++;
                        progress?.Report(new GalleryTransferProgress("正在导入图库", processed, total, added, sourceSample.FileName));
                    }
                }

                // Incremental deltas are exact; reconcile once per gallery so a partially failed
                // sample cannot leave a permanent drift.
                await GalleryStatisticsMaintainer.RefreshGalleryAsync(_db, gallery.Id, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Rollback created galleries on unexpected failure or cancellation to prevent orphaned data
            _logger.LogWarning(ex, "Gallery import failed or cancelled; rolling back {Count} created galleries", createdGalleries.Count);
            foreach (var createdId in createdGalleries)
            {
                try
                {
                    await _galleries.DeleteAsync(createdId, CancellationToken.None).ConfigureAwait(false);
                    var dir = _fileStore.GetGalleryDirectory(projectId, createdId, ensureExists: false);
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogError(cleanupEx, "Failed to cleanup gallery {GalleryId} during import rollback", createdId);
                }
            }
            throw;
        }

        var result = new GalleryTransferResult(failed == 0, total, added, failed, skipped);
        result.Errors.AddRange(errors);
        return result;
    }

    public string GetGalleryDirectory(Guid projectId, Guid galleryId, bool ensureExists)
    {
        return _fileStore.GetGalleryDirectory(projectId, galleryId, ensureExists);
    }

    private async Task<GalleryTransferResult> ImportImagesAsync(
        Guid galleryId,
        IReadOnlyList<string> imageFiles,
        IReadOnlyDictionary<string, string> jsonLookup,
        string stage,
        IProgress<GalleryTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken);
        if (gallery is null)
        {
            var missing = new GalleryTransferResult(false, imageFiles.Count, 0, imageFiles.Count, 0);
            missing.Errors.Add("图库不存在");
            return missing;
        }

        var projectType = (await _projects.GetByIdAsync(gallery.ProjectId, cancellationToken))?.Type;

        if (imageFiles.Count == 0)
        {
            var empty = new GalleryTransferResult(false, 0, 0, 0, 0);
            empty.Errors.Add("未找到支持的图像文件");
            return empty;
        }

        var imagesDirectory = _fileStore.GetGalleryImagesDirectory(gallery.ProjectId, gallery.Id, ensureExists: true);
        var added = 0;
        var failed = 0;
        var skipped = 0;
        var errors = new List<string>();

        for (var index = 0; index < imageFiles.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePath = imageFiles[index];
            var originalName = Path.GetFileName(imagePath);

            try
            {
                var targetFileName = TransferFileNames.GenerateUniqueFileName(imagesDirectory, originalName);
                var sourceJson = await ResolveAnnotationPathAsync(imagePath, jsonLookup, cancellationToken);
                var documentJson = sourceJson is not null
                    ? await File.ReadAllTextAsync(sourceJson, cancellationToken)
                    : null;

                await ImportSampleAsync(
                    gallery, targetFileName,
                    async (stream, token) =>
                    {
                        await using var source = File.OpenRead(imagePath);
                        await source.CopyToAsync(stream, token).ConfigureAwait(false);
                    },
                    documentJson, documentJson is not null, false, null, cancellationToken);
                added++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                errors.Add($"{originalName}: {ex.Message}");
                _logger.LogError(ex, "Failed to import image {ImagePath}", imagePath);
            }
            finally
            {
                progress?.Report(new GalleryTransferProgress(stage, index + 1, imageFiles.Count, added, originalName));
            }
        }

        if (added == 0 && failed == 0)
        {
            skipped = imageFiles.Count;
        }

        if (added > 0)
        {
            // One reconcile for the whole batch (per-sample deltas already ran inside each commit).
            await GalleryStatisticsMaintainer.RefreshGalleryAsync(_db, gallery.Id, cancellationToken).ConfigureAwait(false);
        }

        var result = new GalleryTransferResult(failed == 0 && added > 0, imageFiles.Count, added, failed, skipped);
        result.Errors.AddRange(errors);
        return result;
    }

    private bool TryEnterProjectOperation(Guid projectId, out IDisposable? lease, out string? error)
    {
        try
        {
            lease = _operations.EnterStart(projectId);
            error = null;
            return true;
        }
        catch (ProjectBusyException ex)
        {
            lease = null;
            error = ex.Message;
            return false;
        }
    }

    private static GalleryTransferResult Failed(string message)
    {
        var result = new GalleryTransferResult(false, 0, 0, 0, 0);
        result.Errors.Add(message);
        return result;
    }

    private static IFreeSql BuildSqlite(string filePath, bool autoSync)
    {
        var db = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={filePath};Pooling=true;Max Pool Size=5")
            .UseAutoSyncStructure(autoSync)
            .UseNoneCommandParameter(true)
            .Build();

        if (autoSync)
        {
            db.CodeFirst.SyncStructure(typeof(SqlGallery), typeof(SqlSample), typeof(SqlAnnotation));
        }

        return db;
    }

    private static async Task<string?> ResolveAnnotationPathAsync(string imagePath, IReadOnlyDictionary<string, string> selectedJsonFiles, CancellationToken token)
    {
        // Full paths keep recursively imported folders independent. Never match by basename alone.
        var canonical = Path.GetFullPath(SampleAnnotationFiles.GetPath(imagePath));
        if (selectedJsonFiles.TryGetValue(canonical, out var selected)) return selected;
        return (await SampleAnnotationFiles.ReadAsync(imagePath, null, token)).FilePath;
    }

    private async Task ImportSampleAsync(
        ProjectGallery gallery,
        string targetFileName,
        Func<Stream, CancellationToken, Task> writeImage,
        string? documentJson,
        bool isAnnotated,
        bool hasError,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        // Parse with the same contract as Load before creating any destination files or rows.
        LabelmeDocument? document = null;
        if (documentJson is not null)
        {
            document = LabelmeAnnotationSerializer.Deserialize(documentJson)
                ?? throw new InvalidDataException("标注文档必须是 JSON object。");
        }

        var targetPath = Path.Combine(_fileStore.GetGalleryImagesDirectory(gallery.ProjectId, gallery.Id, true), targetFileName);
        var ownedPaths = new List<string>();
        var committed = false;
        try
        {
            await using (var image = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous))
            {
                ownedPaths.Add(targetPath);
                await writeImage(image, cancellationToken).ConfigureAwait(false);
            }
            if (documentJson is not null)
            {
                var annotationPath = SampleAnnotationFiles.GetPath(targetPath);
                await using var sidecar = new FileStream(annotationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    81920, FileOptions.Asynchronous);
                ownedPaths.Add(annotationPath);
                await using var writer = new StreamWriter(sidecar);
                await writer.WriteAsync(documentJson.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            using var connectionLease = _db.Ado.MasterPool.Get();
            var connection = connectionLease.Value;
            if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _db.Insert(new SqlSample
                {
                    Id = Guid.NewGuid(), GalleryId = gallery.Id, FileName = targetFileName,
                    IsAnnotated = isAnnotated, HasError = hasError, ErrorMessage = errorMessage
                }).WithTransaction(transaction).ExecuteAffrowsAsync(cancellationToken).ConfigureAwait(false);
                if (document is not null)
                {
                    await _db.Insert(new SqlAnnotation
                    {
                        GalleryId = gallery.Id, FileName = targetFileName,
                        Width = document.ImageWidth, Height = document.ImageHeight, DocumentJson = documentJson!
                    }).WithTransaction(transaction).ExecuteAffrowsAsync(cancellationToken).ConfigureAwait(false);
                    await GallerySampleLabelIndexMaintainer.ReplaceLabelsFromDocumentAsync(
                        _db, gallery.Id, targetFileName, documentJson, cancellationToken, transaction).ConfigureAwait(false);
                }
                // Incremental: recounting the gallery per sample made bulk import O(n²).
                // Callers reconcile once at the end with RefreshGalleryAsync.
                await GalleryStatisticsMaintainer.ApplySampleDeltaAsync(
                    _db,
                    gallery.Id,
                    sampleDelta: 1,
                    annotatedDelta: isAnnotated ? 1 : 0,
                    cancellationToken,
                    transaction).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                committed = true;
            }
            catch
            {
                if (connection is not System.Data.SQLite.SQLiteConnection { AutoCommit: true })
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        catch (Exception failure)
        {
            // Commit can throw after SQLite has already applied the transaction (a token that fires
            // between the commit returning and the await resuming). Deleting the files then would
            // strip the image from a sample whose rows are committed, so only clean up when the
            // transaction did not commit — including the AutoCommit rollback case above.
            if (committed)
            {
                throw;
            }

            var cleanupFailures = new List<Exception>();
            foreach (var path in ownedPaths.AsEnumerable().Reverse())
            {
                try { File.Delete(path); }
                catch (Exception ex) { cleanupFailures.Add(new IOException($"导入失败后无法清理 {path}", ex)); }
            }
            if (cleanupFailures.Count > 0)
                throw new AggregateException("导入失败，部分新建文件需要清理。", new[] { failure }.Concat(cleanupFailures));
            if (cancellationToken.IsCancellationRequested && failure is not OperationCanceledException)
                throw new OperationCanceledException("导入样本已取消。", failure, cancellationToken);
            throw;
        }
    }
}
