using System.Text.Json.Nodes;
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

/// <summary>
/// 把一个项目导出成单个 .project 包，或从包里还原出一个新项目。
/// </summary>
/// <remarks>
/// 包里包含项目、标签、图库、样本、标注（整图类别和检测框）、模型、训练任务，以及模型权重文件
/// （<c>.pt</c> / <c>.torchsharp</c> 及其 <c>.info</c> sidecar）。训练过程产物（数据集、日志、中间权重）
/// 可以重新生成，不进包。
/// 导出先写同目录的临时文件，全部成功后才替换目标，写到一半不会毁掉已有的包。
/// 导入或导出只要有一个样本失败就整包回滚，不留下缺图缺标注的半成品。
/// </remarks>
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
        // 临时文件路径要在 try 之外可见，失败时才能清理。
        string? temporaryFilePath = null;
        try
        {
            if (string.IsNullOrWhiteSpace(outputFilePath))
            {
                return Result<ProjectTransferResult>.Failure("导出路径不能为空");
            }

            using var operation = _operations.EnterStart(projectId);
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

            temporaryFilePath = $"{outputFilePath}.{Guid.NewGuid():N}.tmp";
            var counts = await WritePackageAsync(
                project, galleries, labelRows, temporaryFilePath, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            ReplaceFile(temporaryFilePath, outputFilePath);
            temporaryFilePath = null;

            return Result<ProjectTransferResult>.Success(
                new ProjectTransferResult(true, counts.Total, counts.Exported, 0, 0));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export project {ProjectId}", projectId);
            return Result<ProjectTransferResult>.Failure($"导出项目失败：{ex.Message}");
        }
        finally
        {
            DeleteIfExists(temporaryFilePath);
        }
    }

    /// <summary>写出一个完整的包。任何一个样本失败都抛出，由调用方删掉临时文件。</summary>
    private async Task<ExportCounts> WritePackageAsync(
        Project project,
        IReadOnlyList<ProjectGallery> galleries,
        IReadOnlyList<SqlProjectLabel> labelRows,
        string packageFilePath,
        IProgress<ProjectTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var db = ProjectPackage.Create(packageFilePath);

        await db.Insert(new SqlProject
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Type = project.Type,
            SampleCount = project.SampleCount,
            Progress = project.Progress,
            CreatedUtc = project.CreatedAt.UtcDateTime,
            LastUpdatedUtc = project.LastUpdatedAt.UtcDateTime
        }).ExecuteAffrowsAsync(cancellationToken);

        if (labelRows.Count > 0)
        {
            await db.Insert(labelRows).ExecuteAffrowsAsync(cancellationToken);
        }

        // FreeSql/SQLite 不能把本地数组的 .Contains(Guid) 翻译成 SQL，只能按图库逐个取。
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

        var total = sampleRows.Count;
        var processed = 0;
        var exported = 0;

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

                var imagePath = _fileStore.ResolveSampleImagePath(project.Id, gallery.Id, sample.FileName);
                if (imagePath is null)
                {
                    throw new IOException($"缺失图像: {sample.FileName}");
                }

                var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
                var annotationDocument = await _annotations.LoadAsync(gallery.Id, sample.FileName, cancellationToken);

                await db.Insert(new SqlSample
                {
                    Id = sample.Id,
                    GalleryId = gallery.Id,
                    FileName = sample.FileName,
                    IsAnnotated = sample.IsAnnotated,
                    HasError = sample.HasError,
                    ErrorMessage = sample.ErrorMessage
                }).ExecuteAffrowsAsync(cancellationToken);

                await db.Insert(new SqlAnnotation
                {
                    GalleryId = gallery.Id,
                    FileName = sample.FileName,
                    Width = annotationDocument.ImageWidth,
                    Height = annotationDocument.ImageHeight,
                    ImageData = Convert.ToBase64String(bytes),
                    DocumentJson = LabelmeAnnotationSerializer.Serialize(
                        BuildLabelmeDocument(sample.FileName, annotationDocument))
                }).ExecuteAffrowsAsync(cancellationToken);

                exported++;
                processed++;
                progress?.Report(new ProjectTransferProgress(
                    "正在导出项目", processed, total, exported, sample.FileName));
            }
        }

        await CopyModelsAsync(db, project.Id, cancellationToken);
        await CopyTrainingJobsAsync(db, project.Id, cancellationToken);

        return new ExportCounts(total, exported);
    }

    /// <summary>复制模型行，并把权重文件与 .info sidecar 按相对路径收进包。</summary>
    private async Task CopyModelsAsync(IFreeSql package, Guid projectId, CancellationToken cancellationToken)
    {
        var models = await _db.Select<SqlModel>()
            .Where(model => model.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        if (models.Count == 0)
        {
            return;
        }

        var projectDirectory = _fileStore.GetProjectDirectory(projectId);
        var stored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(model.CheckpointPath))
            {
                continue;
            }

            var checkpoint = Path.GetFullPath(model.CheckpointPath);
            var relativePath = PackagePath(Path.GetRelativePath(projectDirectory, checkpoint));
            await StoreFileAsync(package, checkpoint, relativePath, stored, cancellationToken);
            await StoreFileAsync(package, checkpoint + ".info", relativePath + ".info", stored, cancellationToken);
            model.CheckpointPath = relativePath;
        }

        await package.Insert(models).ExecuteAffrowsAsync(cancellationToken);
    }

    /// <summary>复制训练任务行。进行中的任务导出后没有进程可恢复，标记为失败。</summary>
    private async Task CopyTrainingJobsAsync(IFreeSql package, Guid projectId, CancellationToken cancellationToken)
    {
        var jobs = await _db.Select<SqlTrainingJob>()
            .Where(job => job.ProjectId == projectId)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0)
        {
            return;
        }

        foreach (var job in jobs)
        {
            if (job.Status is "Preparing" or "Running")
            {
                job.Status = "Failed";
                job.ProcessId = null;
                job.FailureMessage = "导出时该训练仍在运行，包内不保留运行状态。";
            }
        }

        await package.Insert(jobs).ExecuteAffrowsAsync(cancellationToken);
    }

    /// <summary>
    /// 把一个文件收进包。路径必须落在项目目录内，否则拒绝，避免包把任意磁盘文件带走。
    /// 文件不存在时跳过：权重可能已被单独删除，模型行仍要保留。
    /// </summary>
    private async Task StoreFileAsync(
        IFreeSql package,
        string absolutePath,
        string relativePath,
        HashSet<string> stored,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(absolutePath) || !stored.Add(relativePath))
        {
            return;
        }

        var content = await File.ReadAllBytesAsync(absolutePath, cancellationToken);
        await package.Insert(new SqlPackageFile
        {
            RelativePath = relativePath,
            Content = content
        }).ExecuteAffrowsAsync(cancellationToken);
    }

    public async Task<Result<ProjectTransferResult>> ImportAsync(
        string inputFilePath,
        IProgress<ProjectTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 记录新建的项目，失败或取消时回滚，避免留下空的「导入项目」让重试产生重复。
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

            using var db = ProjectPackage.Open(inputFilePath);
            var sourceProjects = await db.Select<SqlProject>().ToListAsync(cancellationToken);
            if (sourceProjects.Count == 0)
            {
                return Result<ProjectTransferResult>.Failure("未找到项目数据");
            }

            if (sourceProjects.Count != 1)
            {
                return Result<ProjectTransferResult>.Failure("项目包必须只包含一个项目");
            }

            var sourceProject = sourceProjects[0];
            var tables = (await db.Ado.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type = 'table'", cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
            // 导入耗时长：在最后一个样本提交前，新项目不能被删除。
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
            var existingGalleryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 原图库 Id 到新图库 Id 的对应，还原模型与训练任务时要改写它们引用的路径。
            var galleryIdMap = new Dictionary<Guid, Guid>();

            progress?.Report(new ProjectTransferProgress("正在导入项目", 0, total, 0));

            foreach (var galleryRow in galleryRows)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var galleryName = TransferFileNames.MakeUniqueName(galleryRow.Name, existingGalleryNames, "导入图库");
                var gallery = await _galleries.CreateAsync(
                    project.Id, galleryName, galleryRow.Description, cancellationToken);
                galleryIdMap[galleryRow.Id] = gallery.Id;
                var imagesDirectory = _fileStore.GetGalleryImagesDirectory(project.Id, gallery.Id, ensureExists: true);

                var sampleRows = await db.Select<SqlSample>()
                    .Where(sample => sample.GalleryId == galleryRow.Id)
                    .ToListAsync(cancellationToken);

                var importedSamples = new List<GallerySample>();
                foreach (var sampleRow in sampleRows)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 一次只读一行：行里带着 Base64 图像，整库读入会占用几百 MB。
                    var annotationRow = await TransferAnnotationReader
                        .FindAsync(db, galleryRow.Id, sampleRow.FileName, cancellationToken)
                        .ConfigureAwait(false);
                    if (annotationRow is null)
                    {
                        throw new IOException($"缺失标注: {sampleRow.FileName}");
                    }

                    var base64 = TransferPayloads.ExtractBase64(annotationRow.ImageData);
                    if (string.IsNullOrWhiteSpace(base64))
                    {
                        throw new IOException($"缺失图像: {sampleRow.FileName}");
                    }

                    if (!TransferFileNames.TryGetSafeFileName(sampleRow.FileName, out var safeFileName))
                    {
                        throw new IOException($"非法文件名: {sampleRow.FileName}");
                    }

                    var targetFileName = TransferFileNames.GenerateUniqueFileName(imagesDirectory, safeFileName);
                    var targetPath = Path.Combine(imagesDirectory, targetFileName);
                    await File.WriteAllBytesAsync(targetPath, Convert.FromBase64String(base64), cancellationToken);

                    var document = LabelmeAnnotationSerializer.Deserialize(annotationRow.DocumentJson);
                    var imageClass = LabelmeAnnotationSerializer.ReadImageClass(document?.Flags);
                    var shapes = LabelmeAnnotationSerializer.ReadShapes(document);
                    var reviewed = LabelmeAnnotationSerializer.ReadReviewed(document?.Flags);
                    var width = annotationRow.Width > 0 ? annotationRow.Width : 0;
                    var height = annotationRow.Height > 0 ? annotationRow.Height : 0;

                    if (!string.IsNullOrWhiteSpace(imageClass) || shapes.Count > 0 || reviewed)
                    {
                        await _annotations.SaveAsync(
                            gallery.Id,
                            targetFileName,
                            width,
                            height,
                            imageClass,
                            shapes,
                            reviewed,
                            cancellationToken);
                    }

                    importedSamples.Add(new GallerySample(
                        Guid.NewGuid(),
                        gallery.Id,
                        targetFileName,
                        !string.IsNullOrWhiteSpace(imageClass) || shapes.Count > 0 || reviewed || sampleRow.IsAnnotated,
                        sampleRow.HasError,
                        sampleRow.ErrorMessage));

                    added++;
                    processed++;
                    progress?.Report(new ProjectTransferProgress(
                        "正在导入项目", processed, total, added, sampleRow.FileName));
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (importedSamples.Count > 0)
                {
                    await _galleries.SaveAsync(gallery.WithSamples(importedSamples), cancellationToken);
                }
            }

            if (tables.Contains("Models"))
            {
                await RestoreModelsAsync(db, sourceProject.Id, project.Id, galleryIdMap,
                    tables.Contains("PackageFiles"), cancellationToken);
            }

            if (tables.Contains("TrainingJobs"))
            {
                await RestoreTrainingJobsAsync(db, sourceProject.Id, project.Id, galleryIdMap, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            return Result<ProjectTransferResult>.Success(
                new ProjectTransferResult(true, total, added, 0, 0, project.Id));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 先释放自己的租约：回滚删除要进入同一个门禁。
            ReleaseLease(ref projectLease);
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

    /// <summary>还原模型行和权重文件。权重里记录的路径按新项目目录重写。</summary>
    private async Task RestoreModelsAsync(
        IFreeSql package,
        Guid sourceProjectId,
        Guid projectId,
        IReadOnlyDictionary<Guid, Guid> galleryIdMap,
        bool hasPackageFiles,
        CancellationToken cancellationToken)
    {
        var models = await package.Select<SqlModel>()
            .Where(model => model.ProjectId == sourceProjectId)
            .ToListAsync(cancellationToken);
        if (models.Count == 0)
        {
            return;
        }

        var projectDirectory = _fileStore.GetProjectDirectory(projectId, ensureExists: true);
        foreach (var model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            model.Id = Guid.NewGuid();
            model.ProjectId = projectId;
            if (string.IsNullOrWhiteSpace(model.CheckpointPath))
            {
                continue;
            }

            var relativePath = ReadPackagePath(sourceProjectId, model.CheckpointPath);
            model.CheckpointPath = RewritePath(projectDirectory, relativePath, galleryIdMap);
            if (hasPackageFiles)
            {
                await RestoreFileAsync(package, relativePath, model.CheckpointPath, cancellationToken);
                await RestoreFileAsync(package, relativePath + ".info", model.CheckpointPath + ".info", cancellationToken);
            }
        }

        await _db.Insert(models).ExecuteAffrowsAsync(cancellationToken);
    }

    /// <summary>还原训练任务行。任务引用的数据与输出路径重写到新项目目录下。</summary>
    private async Task RestoreTrainingJobsAsync(
        IFreeSql package,
        Guid sourceProjectId,
        Guid projectId,
        IReadOnlyDictionary<Guid, Guid> galleryIdMap,
        CancellationToken cancellationToken)
    {
        var jobs = await package.Select<SqlTrainingJob>()
            .Where(job => job.ProjectId == sourceProjectId)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0)
        {
            return;
        }

        var projectDirectory = _fileStore.GetProjectDirectory(projectId);
        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceJobId = job.Id;
            job.Id = Guid.NewGuid();
            job.ProjectId = projectId;
            var pathIds = new Dictionary<Guid, Guid>(galleryIdMap) { [sourceJobId] = job.Id };
            job.DataRoot = RestoreJobPath(job.DataRoot);
            job.OutputRoot = RestoreJobPath(job.OutputRoot);
            job.BestModelPath = RestoreJobPath(job.BestModelPath);
            job.LastModelPath = RestoreJobPath(job.LastModelPath);
            job.LogFile = RestoreJobPath(job.LogFile);
            job.ConfigurationJson = RemapGalleryReferences(job.ConfigurationJson, galleryIdMap);
            if (job.Status is "Preparing" or "Running")
            {
                job.Status = "Failed";
                job.FailureMessage = "项目包未保留训练进程。";
            }

            job.ProcessId = null;

            string? RestoreJobPath(string? path) => string.IsNullOrWhiteSpace(path)
                ? null
                : RewritePath(projectDirectory, ReadPackagePath(sourceProjectId, path), pathIds);
        }

        await _db.Insert(jobs).ExecuteAffrowsAsync(cancellationToken);
    }

    private static async Task RestoreFileAsync(
        IFreeSql package,
        string relativePath,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var legacyPath = relativePath.Replace('/', '\\');
        var row = await package.Select<SqlPackageFile>()
            .Where(file => file.RelativePath == relativePath || file.RelativePath == legacyPath)
            .FirstAsync(cancellationToken);
        if (row is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await File.WriteAllBytesAsync(targetPath, row.Content, cancellationToken);
        }
    }

    private static string RewritePath(
        string projectDirectory,
        string relativePath,
        IReadOnlyDictionary<Guid, Guid> idMap)
    {
        var segments = relativePath.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            if (Guid.TryParse(segments[index], out var sourceId) && idMap.TryGetValue(sourceId, out var targetId))
            {
                segments[index] = targetId.ToString();
            }
        }

        return Path.Combine(projectDirectory, string.Join(Path.DirectorySeparatorChar, segments));
    }

    private static string ReadPackagePath(Guid sourceProjectId, string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!Path.IsPathRooted(normalized))
        {
            return PackagePath(normalized);
        }

        // Early package builds wrote absolute paths. Only reuse the portion below the source project.
        var marker = $"/projects/{sourceProjectId:D}/";
        var index = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return PackagePath(index >= 0 ? normalized[(index + marker.Length)..] : Path.GetFileName(normalized));
    }

    private static string PackagePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Split('/').Any(segment => segment is "" or "." or ".."
            || segment.EndsWith('.') || segment.EndsWith(' ')
            || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException($"无效的项目包路径：{path}");
        }

        return normalized;
    }

    private static string RemapGalleryReferences(string configuration, IReadOnlyDictionary<Guid, Guid> galleryIdMap)
    {
        if (JsonNode.Parse(configuration) is not JsonObject root)
        {
            return configuration;
        }

        if (TryGetGalleryId(root["GalleryId"], out var galleryId))
        {
            root["GalleryId"] = galleryId;
        }

        if (root["GalleryIds"] is JsonArray galleryIds)
        {
            for (var index = 0; index < galleryIds.Count; index++)
            {
                if (TryGetGalleryId(galleryIds[index], out var targetId))
                {
                    galleryIds[index] = targetId;
                }
            }
        }

        if (root["GalleryRoles"] is JsonObject roles)
        {
            foreach (var (key, role) in roles.ToArray())
            {
                if (Guid.TryParse(key, out var sourceId) && galleryIdMap.TryGetValue(sourceId, out var targetId))
                {
                    roles.Remove(key);
                    roles[targetId.ToString()] = role;
                }
            }
        }

        return root.ToJsonString();

        bool TryGetGalleryId(JsonNode? value, out Guid targetId)
        {
            targetId = default;
            return Guid.TryParse(value?.ToString(), out var id) && galleryIdMap.TryGetValue(id, out targetId);
        }
    }

    private static void ReleaseLease(ref IDisposable? lease)
    {
        lease?.Dispose();
        lease = null;
    }

    /// <summary>
    /// 尽力回滚一个导入到一半的项目。复用仓储的删除（事务内删行，提交后删目录）。
    /// 返回失败文本而不是抛出，让原本的导入错误仍然可见。
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

    /// <summary>用临时文件替换目标。目标已存在时走 Replace，替换失败旧文件还在。</summary>
    private static void ReplaceFile(string temporaryFilePath, string targetFilePath)
    {
        if (File.Exists(targetFilePath))
        {
            var backupFilePath = $"{targetFilePath}.{Guid.NewGuid():N}.bak";
            File.Replace(temporaryFilePath, targetFilePath, backupFilePath, ignoreMetadataErrors: true);
            DeleteIfExists(backupFilePath);
            return;
        }

        File.Move(temporaryFilePath, targetFilePath);
    }

    private static void DeleteIfExists(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            // 清理失败不影响结果：临时文件留在同目录，下次导出不会读它。
        }
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
            Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(document.ImageClass, document.IsReviewed),
            Shapes = LabelmeAnnotationSerializer.ToLabelmeShapes(document.Shapes)
        };
    }

    private readonly record struct ExportCounts(int Total, int Exported);
}
