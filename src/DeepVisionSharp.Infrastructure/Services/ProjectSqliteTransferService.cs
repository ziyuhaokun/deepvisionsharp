using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;
using DeepVisionSharp.Infrastructure.Services.Transfer;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ProjectSqliteTransferService : IProjectSqliteTransferService
{
    private readonly IProjectRepository _projects;
    private readonly IGalleryRepository _galleries;
    private readonly IProjectFileStore _fileStore;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly IProjectLabelService _labels;
    private readonly IFreeSql _db;
    private readonly ProjectOperationGate _operations;
    private readonly ILogger<ProjectSqliteTransferService> _logger;

    public ProjectSqliteTransferService(
        IProjectRepository projects,
        IGalleryRepository galleries,
        IProjectFileStore fileStore,
        IAnnotationWorkflowService annotations,
        IProjectLabelService labels,
        SqliteDb sqliteDb,
        ProjectOperationGate operations,
        ILogger<ProjectSqliteTransferService> logger)
    {
        _projects = projects;
        _galleries = galleries;
        _fileStore = fileStore;
        _annotations = annotations;
        _labels = labels;
        _db = sqliteDb.Instance;
        _operations = operations;
        _logger = logger;
    }

    public async Task<Result<ProjectTransferResult>> ExportAsync(
        Guid projectId,
        string outputFilePath,
        IProgress<ProjectTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                return Result<ProjectTransferResult>.Failure("导出路径不能为空");
            }

            var project = await _projects.GetByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                return Result<ProjectTransferResult>.Failure("项目不存在");
            }

            var galleries = await _galleries.ListByProjectAsync(projectId, cancellationToken);
            var labelRows = await _db.Select<SqlProjectLabel>()
                .Where(label => label.ProjectId == projectId)
                .ToListAsync(cancellationToken);

            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }

            using var db = CreatePackageDatabase(outputFilePath);
            await db.Insert(new SqlProject
            {
                Id = project.Id,
                Name = project.Name,
                Description = project.Description,
                Type = project.Type,
                SampleCount = project.SampleCount,
                Progress = project.Progress,
                LastUpdatedUtc = project.LastUpdatedAt.UtcDateTime
            }).ExecuteAffrowsAsync(cancellationToken);

            if (labelRows.Count > 0)
            {
                await db.Insert(labelRows).ExecuteAffrowsAsync(cancellationToken);
            }

            // FreeSql/SQLite cannot translate local-array .Contains(Guid); load per gallery.
            var sampleRows = new List<SqlSample>();
            foreach (var gallery in galleries)
            {
                var galleryId = gallery.Id;
                var batch = await _db.Select<SqlSample>()
                    .Where(sample => sample.GalleryId == galleryId)
                    .ToListAsync(cancellationToken);
                sampleRows.AddRange(batch);
            }

            var samplesByGallery = sampleRows
                .GroupBy(sample => sample.GalleryId)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var total = galleries.Sum(gallery => gallery.SampleCount);
            var processed = 0;
            var exported = 0;
            var failed = 0;
            var skipped = 0;
            var errors = new List<string>();

            progress?.Report(new ProjectTransferProgress("正在导出项目", 0, total, 0));

            foreach (var gallery in galleries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await db.Insert(new SqlGallery
                {
                    Id = gallery.Id,
                    ProjectId = gallery.ProjectId,
                    Name = gallery.Name,
                    Description = gallery.Description,
                    SampleCount = gallery.SampleCount,
                    AnnotatedCount = gallery.AnnotatedCount,
                    LastUpdatedUtc = gallery.LastUpdatedAt.UtcDateTime
                }).ExecuteAffrowsAsync(cancellationToken);

                var gallerySamples = samplesByGallery.TryGetValue(gallery.Id, out var samples)
                    ? samples
                    : [];

                foreach (var sample in gallerySamples)
                {
                    cancellationToken.ThrowIfCancellationRequested();

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

                        string? imageData = null;
                        var imagePath = _fileStore.ResolveSampleImagePath(project.Id, gallery.Id, sample.FileName);
                        if (imagePath is not null)
                        {
                            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                            imageData = Convert.ToBase64String(bytes);
                        }
                        else
                        {
                            skipped++;
                            errors.Add($"缺失图像: {sample.FileName}");
                        }

                        var annotationDocument = await _annotations.LoadAsync(gallery.Id, sample.FileName, cancellationToken);
                        var labelme = BuildLabelmeDocument(sample.FileName, annotationDocument);

                        await db.Insert(new SqlAnnotation
                        {
                            GalleryId = gallery.Id,
                            FileName = sample.FileName,
                            Width = annotationDocument.ImageWidth,
                            Height = annotationDocument.ImageHeight,
                            ImageData = imageData,
                            DocumentJson = LabelmeAnnotationSerializer.Serialize(labelme)
                        }).ExecuteAffrowsAsync(cancellationToken);

                        exported++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"{sample.FileName}: {ex.Message}");
                        _logger.LogError(ex, "Failed to export sample {FileName}", sample.FileName);
                    }
                    finally
                    {
                        processed++;
                        progress?.Report(new ProjectTransferProgress("正在导出项目", processed, total, exported, sample.FileName));
                    }
                }
            }

            var result = new ProjectTransferResult(failed == 0, total, exported, failed, skipped);
            result.Errors.AddRange(errors);
            return Result<ProjectTransferResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export project {ProjectId}", projectId);
            return Result<ProjectTransferResult>.Failure($"导出项目失败：{ex.Message}");
        }
    }

    public async Task<Result<ProjectTransferResult>> ImportAsync(
        string inputFilePath,
        IProgress<ProjectTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Tracks the project created below so a failed or cancelled import can roll itself back
        // instead of leaving an empty "导入项目" entry that makes retries produce duplicates.
        Guid? createdProjectId = null;
        IDisposable? projectLease = null;
        try
        {
            if (string.IsNullOrWhiteSpace(inputFilePath))
            {
                return Result<ProjectTransferResult>.Failure("导入文件不能为空");
            }

            if (!File.Exists(inputFilePath))
            {
                return Result<ProjectTransferResult>.Failure("导入文件不存在");
            }

            if (!IsSqliteProjectPackage(inputFilePath))
            {
                return Result<ProjectTransferResult>.Failure("不是有效的 SQLite 项目包，请使用 .project 文件");
            }

            using var db = OpenPackageDatabase(inputFilePath);
            var sourceProjects = await db.Select<SqlProject>().ToListAsync(cancellationToken);
            if (sourceProjects.Count == 0)
            {
                return Result<ProjectTransferResult>.Failure("未找到项目数据");
            }

            var sourceProject = sourceProjects[0];
            var type = ProjectTypes.MigrateImportedType(sourceProject.Type);
            ProjectCapabilityCatalog.EnsureActive(type);
            var existingNames = new HashSet<string>(
                (await _projects.ListAsync(cancellationToken)).Select(project => project.Name),
                StringComparer.OrdinalIgnoreCase);

            var project = await _projects.CreateAsync(
                TransferFileNames.MakeUniqueName(sourceProject.Name, existingNames, "导入项目"),
                sourceProject.Description,
                type,
                cancellationToken);
            createdProjectId = project.Id;
            // Long import: keep the new project undeletable until the last sample is committed.
            projectLease = _operations.EnterStart(project.Id);

            var labelRows = await db.Select<SqlProjectLabel>()
                .Where(label => label.ProjectId == sourceProject.Id)
                .ToListAsync(cancellationToken);

            foreach (var label in labelRows)
            {
                if (string.IsNullOrWhiteSpace(label.Name))
                {
                    continue;
                }

                await _labels.AddAsync(project.Id, label.Name, label.Color, cancellationToken);
            }

            var galleryRows = await db.Select<SqlGallery>()
                .Where(gallery => gallery.ProjectId == sourceProject.Id)
                .ToListAsync(cancellationToken);

            var total = 0;
            foreach (var galleryRow in galleryRows)
            {
                total += (int)await db.Select<SqlSample>()
                    .Where(sample => sample.GalleryId == galleryRow.Id)
                    .CountAsync(cancellationToken);
            }

            var processed = 0;
            var added = 0;
            var failed = 0;
            var skipped = 0;
            var errors = new List<string>();
            var existingGalleryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            progress?.Report(new ProjectTransferProgress("正在导入项目", 0, total, 0));

            foreach (var galleryRow in galleryRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var galleryName = TransferFileNames.MakeUniqueName(galleryRow.Name, existingGalleryNames, "导入图库");
                var gallery = await _galleries.CreateAsync(
                    project.Id,
                    galleryName,
                    galleryRow.Description,
                    cancellationToken);
                var imagesDirectory = _fileStore.GetGalleryImagesDirectory(project.Id, gallery.Id, ensureExists: true);

                var sampleRows = await db.Select<SqlSample>()
                    .Where(sample => sample.GalleryId == galleryRow.Id)
                    .ToListAsync(cancellationToken);

                var importedSamples = new List<GallerySample>();

                foreach (var sampleRow in sampleRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // One row at a time: the row carries a Base64 image, so a gallery-sized
                        // dictionary (or even a page) costs hundreds of MB.
                        var annotationRow = await TransferAnnotationReader
                            .FindAsync(db, galleryRow.Id, sampleRow.FileName, cancellationToken)
                            .ConfigureAwait(false);
                        if (annotationRow is null)
                        {
                            skipped++;
                            errors.Add($"缺失标注: {sampleRow.FileName}");
                            continue;
                        }

                        var base64 = TransferPayloads.ExtractBase64(annotationRow.ImageData);
                        if (string.IsNullOrWhiteSpace(base64))
                        {
                            skipped++;
                            errors.Add($"缺失图像: {sampleRow.FileName}");
                            continue;
                        }

                        if (!TransferFileNames.TryGetSafeFileName(sampleRow.FileName, out var safeFileName))
                        {
                            failed++;
                            errors.Add($"非法文件名: {sampleRow.FileName}");
                            continue;
                        }

                        var targetFileName = TransferFileNames.GenerateUniqueFileName(imagesDirectory, safeFileName);
                        var targetPath = Path.Combine(imagesDirectory, targetFileName);
                        await File.WriteAllBytesAsync(targetPath, Convert.FromBase64String(base64), cancellationToken);

                        var imageClass = AnnotationDocumentLabelReader.ReadImageClass(annotationRow.DocumentJson);
                        var width = annotationRow.Width > 0 ? annotationRow.Width : 0;
                        var height = annotationRow.Height > 0 ? annotationRow.Height : 0;

                        if (!string.IsNullOrWhiteSpace(imageClass))
                        {
                            await _annotations.SaveAsync(
                                gallery.Id,
                                targetFileName,
                                width,
                                height,
                                imageClass,
                                cancellationToken);
                        }

                        importedSamples.Add(new GallerySample(
                            Guid.NewGuid(),
                            gallery.Id,
                            targetFileName,
                            !string.IsNullOrWhiteSpace(imageClass) || sampleRow.IsAnnotated,
                            sampleRow.HasError,
                            sampleRow.ErrorMessage));

                        added++;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Cancellation must reach the outer handler so the project is rolled back,
                        // instead of being counted as a per-sample failure.
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"{sampleRow.FileName}: {ex.Message}");
                        _logger.LogError(ex, "Failed to import sample {FileName}", sampleRow.FileName);
                    }
                    finally
                    {
                        processed++;
                        progress?.Report(new ProjectTransferProgress("正在导入项目", processed, total, added, sampleRow.FileName));
                    }
                }

                if (importedSamples.Count > 0)
                {
                    await _galleries.SaveAsync(gallery.WithSamples(importedSamples), cancellationToken);
                }
            }

            var result = new ProjectTransferResult(failed == 0, total, added, failed, skipped, project.Id);
            result.Errors.AddRange(errors);
            return Result<ProjectTransferResult>.Success(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Release our own lease first: the rollback delete enters the same gate.
            ReleaseLease(ref projectLease);
            // Cancelling an import means "nothing happened": drop the half-built project.
            await TryRollbackProjectAsync(createdProjectId).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            ReleaseLease(ref projectLease);
            var rollbackError = await TryRollbackProjectAsync(createdProjectId).ConfigureAwait(false);
            _logger.LogError(ex, "Failed to import project from {FilePath}", inputFilePath);
            var message = $"导入项目失败：{ex.Message}";
            if (rollbackError is not null)
            {
                message += $"（已创建的项目未能清理：{rollbackError}，请手动删除）";
            }

            return Result<ProjectTransferResult>.Failure(message);
        }
        finally
        {
            ReleaseLease(ref projectLease);
        }
    }

    private static void ReleaseLease(ref IDisposable? lease)
    {
        lease?.Dispose();
        lease = null;
    }

    /// <summary>
    /// Best-effort compensation for a partially imported project. Reuses the repository delete
    /// (transactional rows + post-commit directory cleanup). Returns the failure text instead of
    /// throwing so the original import error stays visible to the caller.
    /// </summary>
    private async Task<string?> TryRollbackProjectAsync(Guid? projectId)
    {
        if (projectId is not Guid id)
        {
            return null;
        }

        try
        {
            await _projects.DeleteAsync(id, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("Rolled back partially imported project {ProjectId}", id);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rollback of partially imported project {ProjectId} failed", id);
            return ex.Message;
        }
    }

    private static IFreeSql CreatePackageDatabase(string outputFilePath)
    {
        var db = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={outputFilePath};Pooling=true;Max Pool Size=5")
            .UseAutoSyncStructure(true)
            .UseNoneCommandParameter(true)
            .Build();

        db.CodeFirst.SyncStructure(
            typeof(SqlProject),
            typeof(SqlProjectLabel),
            typeof(SqlGallery),
            typeof(SqlSample),
            typeof(SqlAnnotation));

        return db;
    }

    private static IFreeSql OpenPackageDatabase(string inputFilePath)
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={inputFilePath};Pooling=true;Max Pool Size=5")
            .UseAutoSyncStructure(false)
            .UseNoneCommandParameter(true)
            .Build();
    }

    private static bool IsSqliteProjectPackage(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            Span<byte> header = stackalloc byte[16];
            var read = stream.Read(header);
            return read >= 16
                && header[0] == (byte)'S'
                && header[1] == (byte)'Q'
                && header[2] == (byte)'L'
                && header[3] == (byte)'i'
                && header[4] == (byte)'t'
                && header[5] == (byte)'e';
        }
        catch
        {
            return false;
        }
    }

    private static LabelmeDocument BuildLabelmeDocument(string fileName, AnnotationDocument document)
    {
        return new LabelmeDocument
        {
            Version = "5.0.1",
            ImagePath = Path.GetFileName(fileName),
            ImageWidth = (int)Math.Round(Math.Max(0, document.ImageWidth)),
            ImageHeight = (int)Math.Round(Math.Max(0, document.ImageHeight)),
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(document.ImageClass)
        };
    }
}
