using System.Collections.Concurrent;
using System.Data;
using FreeSql;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class AnnotationWorkflowService : IAnnotationWorkflowService
{
    private readonly IGalleryRepository _galleries;
    private readonly IProjectFileStore _fileStore;
    private readonly IFreeSql _db;
    private readonly ILogger<AnnotationWorkflowService> _logger;

    public AnnotationWorkflowService(
        IGalleryRepository galleries,
        IProjectFileStore fileStore,
        SqliteDb sqliteDb,
        ILogger<AnnotationWorkflowService> logger)
    {
        _galleries = galleries;
        _fileStore = fileStore;
        _db = sqliteDb.Instance;
        _logger = logger;
    }

    internal AnnotationWorkflowService(
        IGalleryRepository galleries,
        IProjectFileStore fileStore,
        IFreeSql db,
        ILogger<AnnotationWorkflowService> logger)
    {
        _galleries = galleries;
        _fileStore = fileStore;
        _db = db;
        _logger = logger;
    }

    public async Task<AnnotationDocument> LoadAsync(
        Guid galleryId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false);
        if (gallery is null)
        {
            return new AnnotationDocument(0, 0);
        }

        var documentJson = await ReadDocumentJsonAsync(gallery, fileName, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            return new AnnotationDocument(0, 0);
        }

        try
        {
            var document = LabelmeAnnotationSerializer.Deserialize(documentJson);
            if (document is null)
            {
                return new AnnotationDocument(0, 0);
            }

            return new AnnotationDocument(
                document.ImageWidth,
                document.ImageHeight,
                LabelmeAnnotationSerializer.ReadImageClass(document.Flags));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse annotation document for {FileName} in gallery {GalleryId}", fileName, galleryId);
            return new AnnotationDocument(0, 0);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> LoadImageClassesBatchAsync(
        Guid galleryId,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken = default)
    {
        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (gallery is null) return result;

        var targetNames = fileNames is { Count: > 0 }
            ? fileNames.Select(Path.GetFileName).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name!).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : null;

        var annotationQuery = _db.Select<SqlAnnotation>().Where(row => row.GalleryId == galleryId);
        if (targetNames is not null)
        {
            annotationQuery = annotationQuery.Where(row => targetNames.Contains(row.FileName));
        }
        var rows = await annotationQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        var documents = rows.ToDictionary(row => row.FileName, row => row.DocumentJson, StringComparer.OrdinalIgnoreCase);

        var names = targetNames
            ?? (await _db.Select<SqlSample>().Where(row => row.GalleryId == galleryId)
                .ToListAsync(row => row.FileName, cancellationToken).ConfigureAwait(false)).Concat(documents.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var indexQuery = _db.Select<SqlSampleLabelIndex>().Where(row => row.GalleryId == galleryId);
        if (targetNames is not null)
        {
            indexQuery = indexQuery.Where(row => targetNames.Contains(row.FileName));
        }
        var indexes = await indexQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var name in names.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            documents.TryGetValue(name, out var databaseJson);
            var path = _fileStore.ResolveSampleImagePath(gallery.ProjectId, galleryId, name);
            var content = await SampleAnnotationFiles.ReadAsync(path, databaseJson, cancellationToken).ConfigureAwait(false);
            var imageClass = AnnotationDocumentLabelReader.ReadImageClass(content.Json);
            if (!string.IsNullOrWhiteSpace(imageClass)) result[name] = imageClass;
            else if (string.IsNullOrWhiteSpace(content.Json))
            {
                var label = indexes.FirstOrDefault(row => string.Equals(row.FileName, name, StringComparison.OrdinalIgnoreCase))?.Label;
                if (!string.IsNullOrWhiteSpace(label)) result[name] = label.Trim();
            }
        }
        return result;
    }

    /// <summary>
    /// Serialises writes per sample. Two saves for the same image (a debounced autosave overlapping
    /// an explicit save, or two windows) would otherwise race the File.Exists → Replace/Move check
    /// and could commit the database from one and the sidecar from the other.
    /// </summary>
    /// <remarks>
    /// Entries are kept for the process lifetime: removing one while another caller holds the
    /// reference would let two callers end up on different semaphores for the same sample. The map
    /// is bounded by the number of distinct samples saved, which is small.
    /// </remarks>
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _sampleSaveGates =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task SaveAsync(
        Guid galleryId,
        string fileName,
        double imageWidth,
        double imageHeight,
        string? imageClass = null,
        CancellationToken cancellationToken = default)
    {
        var gateKey = $"{galleryId:N}:{Path.GetFileName(fileName)}";
        var gate = _sampleSaveGates.GetOrAdd(gateKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(galleryId, fileName, imageWidth, imageHeight, imageClass, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveBatchAsync(
        Guid galleryId,
        IReadOnlyList<AnnotationSaveItem> items,
        CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) return;

        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("图库不存在。");

        var imagesDir = _fileStore.GetGalleryImagesDirectory(gallery.ProjectId, gallery.Id, ensureExists: true);
        var preparedFiles = new List<(string TempPath, string FinalPath, string BackupPath, bool HadCanonical)>();
        var annotationsToInsert = new List<SqlAnnotation>();
        var fileNames = new List<string>();
        var newAnnotationsState = new Dictionary<string, (bool HasAnnotations, string? NormalizedClass)>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var safeFileName = Path.GetFileName(item.FileName);
                fileNames.Add(safeFileName);

                var normalizedClass = string.IsNullOrWhiteSpace(item.ImageClass) ? null : item.ImageClass.Trim();
                newAnnotationsState[safeFileName] = (normalizedClass is not null, normalizedClass);

                var document = new LabelmeDocument
                {
                    Version = "5.0.1",
                    ImagePath = safeFileName,
                    ImageData = null,
                    ImageWidth = (int)Math.Round(Math.Max(0, item.ImageWidth)),
                    ImageHeight = (int)Math.Round(Math.Max(0, item.ImageHeight)),
                    Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(item.ImageClass)
                };

                var documentJson = LabelmeAnnotationSerializer.Serialize(document);
                var sampleImagePath = _fileStore.ResolveSampleImagePath(gallery.ProjectId, gallery.Id, safeFileName)
                    ?? Path.Combine(imagesDir, safeFileName);
                var annotationPath = SampleAnnotationFiles.GetPath(sampleImagePath);
                var operationId = Guid.NewGuid().ToString("N");
                var tempPath = annotationPath + "." + operationId + ".tmp";
                var backupPath = annotationPath + "." + operationId + ".bak";

                await File.WriteAllTextAsync(tempPath, documentJson, cancellationToken).ConfigureAwait(false);
                preparedFiles.Add((tempPath, annotationPath, backupPath, File.Exists(annotationPath)));

                annotationsToInsert.Add(new SqlAnnotation
                {
                    GalleryId = gallery.Id,
                    FileName = safeFileName,
                    Width = document.ImageWidth,
                    Height = document.ImageHeight,
                    ImageData = null,
                    DocumentJson = documentJson
                });
            }

            using var connectionLease = _db.Ado.MasterPool.Get();
            var connection = connectionLease.Value;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _db.Delete<SqlAnnotation>()
                    .WithTransaction(transaction)
                    .Where(a => a.GalleryId == gallery.Id && fileNames.Contains(a.FileName))
                    .ExecuteAffrowsAsync(cancellationToken);

                await _db.Insert(annotationsToInsert)
                    .WithTransaction(transaction)
                    .ExecuteAffrowsAsync(cancellationToken);

                var previousSamples = await _db.Select<SqlSample>()
                    .WithTransaction(transaction)
                    .Where(s => s.GalleryId == gallery.Id && fileNames.Contains(s.FileName))
                    .ToListAsync(cancellationToken).ConfigureAwait(false);

                var previousAnnotatedMap = previousSamples.ToDictionary(s => s.FileName, s => s.IsAnnotated, StringComparer.OrdinalIgnoreCase);

                var annotatedDelta = 0;
                foreach (var (fileName, (hasAnnotations, normalizedClass)) in newAnnotationsState)
                {
                    await GallerySampleLabelIndexMaintainer.ReplaceLabelsAsync(
                        _db,
                        gallery.Id,
                        fileName,
                        normalizedClass is null ? Array.Empty<string>() : [normalizedClass],
                        cancellationToken,
                        transaction);

                    await _db.Update<SqlSample>()
                        .WithTransaction(transaction)
                        .Set(s => s.IsAnnotated, hasAnnotations)
                        .Where(s => s.GalleryId == gallery.Id && s.FileName == fileName)
                        .ExecuteAffrowsAsync(cancellationToken);

                    var wasAnnotated = previousAnnotatedMap.TryGetValue(fileName, out var was) && was;
                    if (hasAnnotations != wasAnnotated)
                    {
                        annotatedDelta += hasAnnotations ? 1 : -1;
                    }
                }

                if (annotatedDelta != 0)
                {
                    await GalleryStatisticsMaintainer.ApplySampleDeltaAsync(
                        _db,
                        gallery.Id,
                        sampleDelta: 0,
                        annotatedDelta: annotatedDelta,
                        cancellationToken,
                        transaction).ConfigureAwait(false);
                }

                foreach (var (tempPath, finalPath, backupPath, hadCanonical) in preparedFiles)
                {
                    if (hadCanonical)
                    {
                        File.Replace(tempPath, finalPath, backupPath, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, finalPath, overwrite: false);
                    }
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                if (connection is not System.Data.SQLite.SQLiteConnection { AutoCommit: true })
                {
                    await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            foreach (var (tempPath, _, backupPath, _) in preparedFiles)
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                try { if (File.Exists(backupPath)) File.Delete(backupPath); } catch { }
            }
        }

        _logger.LogDebug("Batch saved annotations for {Count} samples in gallery {GalleryId}", items.Count, galleryId);
    }

    private async Task SaveCoreAsync(
        Guid galleryId,
        string fileName,
        double imageWidth,
        double imageHeight,
        string? imageClass,
        CancellationToken cancellationToken)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("图库不存在。");

        var safeFileName = Path.GetFileName(fileName);
        var document = new LabelmeDocument
        {
            Version = "5.0.1",
            ImagePath = safeFileName,
            ImageData = null,
            ImageWidth = (int)Math.Round(Math.Max(0, imageWidth)),
            ImageHeight = (int)Math.Round(Math.Max(0, imageHeight)),
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(imageClass)
        };

        var documentJson = LabelmeAnnotationSerializer.Serialize(document);
        var sampleImagePath = _fileStore.ResolveSampleImagePath(gallery.ProjectId, gallery.Id, safeFileName)
            ?? Path.Combine(_fileStore.GetGalleryImagesDirectory(gallery.ProjectId, gallery.Id, ensureExists: true), safeFileName);
        var annotationPath = SampleAnnotationFiles.GetPath(sampleImagePath);
        var operationId = Guid.NewGuid().ToString("N");
        var tempAnnotationPath = annotationPath + "." + operationId + ".tmp";
        var backupPath = annotationPath + "." + operationId + ".bak";
        var normalizedClass = string.IsNullOrWhiteSpace(imageClass) ? null : imageClass.Trim();
        var hasAnnotations = normalizedClass is not null;
        var promoted = false;
        var hadCanonical = false;

        try
        {
            await File.WriteAllTextAsync(tempAnnotationPath, documentJson, cancellationToken).ConfigureAwait(false);
            using var connectionLease = _db.Ado.MasterPool.Get();
            var connection = connectionLease.Value;
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _db.Delete<SqlAnnotation>()
                    .WithTransaction(transaction)
                    .Where(item => item.GalleryId == gallery.Id && item.FileName == safeFileName)
                    .ExecuteAffrowsAsync(cancellationToken);

                await _db.Insert(new SqlAnnotation
                    {
                        GalleryId = gallery.Id,
                        FileName = safeFileName,
                        Width = document.ImageWidth,
                        Height = document.ImageHeight,
                        ImageData = null,
                        DocumentJson = documentJson
                    })
                    .WithTransaction(transaction)
                    .ExecuteAffrowsAsync(cancellationToken);

                await GallerySampleLabelIndexMaintainer.ReplaceLabelsAsync(
                    _db,
                    gallery.Id,
                    safeFileName,
                    normalizedClass is null ? Array.Empty<string>() : new[] { normalizedClass },
                    cancellationToken,
                    transaction);

                var previousSample = await _db.Select<SqlSample>().WithTransaction(transaction)
                    .Where(item => item.GalleryId == gallery.Id && item.FileName == safeFileName)
                    .FirstAsync(cancellationToken).ConfigureAwait(false);
                var wasAnnotated = previousSample?.IsAnnotated ?? false;

                var updated = await _db.Update<SqlSample>().WithTransaction(transaction)
                    .Set(item => item.IsAnnotated, hasAnnotations)
                    .Where(item => item.GalleryId == gallery.Id && item.FileName == safeFileName)
                    .ExecuteAffrowsAsync(cancellationToken).ConfigureAwait(false);
                if (updated > 0)
                {
                    // Incremental: a per-image save must not recount the whole gallery.
                    await GalleryStatisticsMaintainer.ApplySampleDeltaAsync(
                        _db,
                        gallery.Id,
                        sampleDelta: 0,
                        annotatedDelta: hasAnnotations == wasAnnotated ? 0 : hasAnnotations ? 1 : -1,
                        cancellationToken,
                        transaction).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                hadCanonical = File.Exists(annotationPath);
                if (hadCanonical)
                    File.Replace(tempAnnotationPath, annotationPath, backupPath, ignoreMetadataErrors: true);
                else
                    File.Move(tempAnnotationPath, annotationPath, overwrite: false);
                promoted = true;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                var recoveryFailures = new List<Exception>();
                try
                {
                    // A rejected SQLite COMMIT may already have rolled the transaction back.
                    if (connection is not System.Data.SQLite.SQLiteConnection { AutoCommit: true })
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) { recoveryFailures.Add(ex); }
                if (promoted)
                {
                    try
                    {
                        if (hadCanonical) File.Move(backupPath, annotationPath, overwrite: true);
                        else File.Delete(annotationPath);
                    }
                    catch (Exception ex) { recoveryFailures.Add(ex); }
                }
                if (recoveryFailures.Count > 0)
                {
                    throw new AggregateException(
                        $"保存标注失败，自动恢复未完成；请保留并检查 {annotationPath} 与备份 {backupPath}。",
                        new[] { failure }.Concat(recoveryFailures));
                }
                // FreeSql may wrap a provider's cancelled task in a plain Exception.
                if (cancellationToken.IsCancellationRequested && failure is not OperationCanceledException)
                    throw new OperationCanceledException("保存标注已取消。", failure, cancellationToken);
                throw;
            }
        }
        finally
        {
            TryDeleteFile(tempAnnotationPath);
        }

        // The database and sidecar are committed. Cleanup must not report a failed save.
        try { if (File.Exists(backupPath)) File.Delete(backupPath); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Saved annotation but could not remove backup {BackupPath}", backupPath);
        }

        _logger.LogDebug(
            "Saved image class {ImageClass} for {FileName} in gallery {GalleryId}",
            normalizedClass ?? "<none>",
            safeFileName,
            gallery.Id);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of staging files.
        }
    }

    public async Task DeleteSampleAsync(Guid galleryId, string fileName, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("图库不存在。");

        var safeFileName = Path.GetFileName(fileName);
        var imagePath = _fileStore.ResolveSampleImagePath(gallery.ProjectId, gallery.Id, safeFileName);

        // Delete the rows first. Removing files first is irreversible: a failure or cancellation
        // afterwards leaves the gallery listing a sample whose image is gone, and nothing can repair
        // it. Rows-first means the worst case is an unreferenced file, which the next import or a
        // manual sweep can remove.
        await _db.Delete<SqlAnnotation>()
            .Where(item => item.GalleryId == gallery.Id && item.FileName == safeFileName)
            .ExecuteAffrowsAsync(cancellationToken);

        await GallerySampleLabelIndexMaintainer.DeleteSampleAsync(_db, gallery.Id, safeFileName, cancellationToken).ConfigureAwait(false);

        if (!await _galleries.RemoveSampleByFileNameAsync(gallery.Id, safeFileName, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (imagePath is not null)
        {
            // Resolve ownership before removing the image (which changes stem-collision detection).
            await SampleAnnotationFiles.PreserveLegacyCollisionAsync(_db, gallery.Id, imagePath, cancellationToken).ConfigureAwait(false);
            var legacy = await SampleAnnotationFiles.FindOwnedLegacyAsync(imagePath, cancellationToken).ConfigureAwait(false);
            DeleteIfExists(SampleAnnotationFiles.GetPath(imagePath));
            DeleteIfExists(legacy);
        }
        DeleteIfExists(imagePath);

        _logger.LogInformation("Deleted sample {FileName} from gallery {GalleryId}", safeFileName, gallery.Id);
    }

    public string GetImagePath(Guid projectId, Guid galleryId, string fileName)
    {
        return _fileStore.ResolveSampleImagePath(projectId, galleryId, fileName)
            ?? Path.Combine(_fileStore.GetGalleryImagesDirectory(projectId, galleryId, ensureExists: false), Path.GetFileName(fileName));
    }

    private async Task<string?> ReadDocumentJsonAsync(ProjectGallery gallery, string fileName, CancellationToken cancellationToken)
    {
        var safeFileName = Path.GetFileName(fileName);
        var imagePath = _fileStore.ResolveSampleImagePath(gallery.ProjectId, gallery.Id, safeFileName);
        var row = await _db.Select<SqlAnnotation>()
            .Where(item => item.GalleryId == gallery.Id && item.FileName == safeFileName)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        return (await SampleAnnotationFiles.ReadAsync(imagePath, row?.DocumentJson, cancellationToken).ConfigureAwait(false)).Json;
    }

    private static void DeleteIfExists(string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}

