using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.Datasets;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class DetectionDatasetExportService : IDetectionDatasetExportService
{
    private readonly IGalleryRepository _galleries;
    private readonly IProjectRepository _projects;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly IProjectFileStore _fileStore;
    private readonly ILogger<DetectionDatasetExportService> _logger;

    public DetectionDatasetExportService(
        IGalleryRepository galleries,
        IProjectRepository projects,
        IAnnotationWorkflowService annotations,
        IProjectFileStore fileStore,
        ILogger<DetectionDatasetExportService> logger)
    {
        _galleries = galleries;
        _projects = projects;
        _annotations = annotations;
        _fileStore = fileStore;
        _logger = logger;
    }

    public async Task<Result<DetectionDatasetExportResult>> ExportAsync(
        Guid projectId,
        IReadOnlyList<Guid> galleryIds,
        IReadOnlyDictionary<Guid, GalleryDatasetRole> galleryRoles,
        TrainingDatasetSplitMode splitMode,
        double validationRatio,
        IReadOnlyList<string> classes,
        string outputRoot,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (project is null)
            {
                return Result<DetectionDatasetExportResult>.Failure("项目不存在。");
            }

            var selectedIds = galleryIds.Where(id => id != Guid.Empty).Distinct().ToArray();
            if (selectedIds.Length == 0)
            {
                return Result<DetectionDatasetExportResult>.Failure("请至少选择一个图库。");
            }

            var effectiveClasses = classes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (effectiveClasses.Length == 0)
            {
                return Result<DetectionDatasetExportResult>.Failure("目标检测训练至少需要 1 个类别。");
            }

            var classIndexes = effectiveClasses
                .Select((name, index) => (name, index))
                .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);

            var samples = new List<DetectionSample>();
            var skippedUnreviewed = 0;
            var skippedMissingImage = 0;
            var skippedBoxes = 0;
            var total = 0;

            foreach (var galleryId in selectedIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken).ConfigureAwait(false);
                if (gallery is null || gallery.ProjectId != projectId)
                {
                    return Result<DetectionDatasetExportResult>.Failure("所选图库不存在或不属于当前项目。");
                }

                var fileNames = await _galleries.ListSampleFileNamesAsync(galleryId, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                total += fileNames.Count;
                var documents = await _annotations
                    .LoadDocumentsBatchAsync(galleryId, fileNames, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var fileName in fileNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    documents.TryGetValue(fileName, out var document);
                    if (document is null || !document.HasAnnotations)
                    {
                        skippedUnreviewed++;
                        continue;
                    }

                    var source = _fileStore.ResolveSampleImagePath(projectId, galleryId, fileName);
                    if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    {
                        skippedMissingImage++;
                        continue;
                    }

                    var (width, height) = document.ImageWidth > 0 && document.ImageHeight > 0
                        ? (document.ImageWidth, document.ImageHeight)
                        : ReadImageSize(source);
                    if (width <= 0 || height <= 0)
                    {
                        skippedMissingImage++;
                        continue;
                    }

                    var boxes = new List<(int ClassIndex, AnnotationShape Shape)>();
                    foreach (var shape in document.Boxes)
                    {
                        if (!classIndexes.TryGetValue(shape.Label.Trim(), out var classIndex))
                        {
                            skippedBoxes++;
                            continue;
                        }

                        var clamped = shape.ClampTo(width, height);
                        if (clamped is null)
                        {
                            skippedBoxes++;
                            continue;
                        }

                        boxes.Add((classIndex, clamped));
                    }

                    samples.Add(new DetectionSample(galleryId, fileName, source, width, height, boxes));
                }
            }

            if (samples.Count == 0)
            {
                return Result<DetectionDatasetExportResult>.Failure("没有可用的检测样本（需要已标注或已标记无目标的图像）。");
            }

            var byPurpose = splitMode == TrainingDatasetSplitMode.PurposeAssignment;
            var assignments = byPurpose
                ? AssignByPurpose(samples, galleryRoles)
                : AssignAutomatically(samples, validationRatio);
            if (assignments.IsFailure || assignments.Value is null)
            {
                return Result<DetectionDatasetExportResult>.Failure(assignments.Error ?? "检测数据集拆分失败。");
            }

            DatasetOutputDirectoryGuard.PrepareEmpty(outputRoot);
            foreach (var split in new[] { "train", "val" })
            {
                Directory.CreateDirectory(Path.Combine(outputRoot, split, "images"));
                Directory.CreateDirectory(Path.Combine(outputRoot, split, "labels"));
            }

            var manifestWriter = new DatasetManifestWriter(outputRoot);
            var trainingBoxes = 0;
            var validationBoxes = 0;
            foreach (var assignment in assignments.Value)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sample = assignment.Sample;
                var extension = Path.GetExtension(sample.FileName);
                // YOLO pairs image and label by stem. Hash the full sample identity, including
                // the extension, to get a stable stem within the filesystem name limit.
                var identity = $"{sample.GalleryId:N}/{sample.FileName}";
                var stem = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                var targetImage = Path.Combine(outputRoot, assignment.Split, "images", stem + extension);
                var targetLabel = Path.Combine(outputRoot, assignment.Split, "labels", stem + ".txt");
                var labelText = YoloDatasetFormat.FormatLabels(sample.Boxes, sample.ImageWidth, sample.ImageHeight);
                await File.WriteAllTextAsync(targetLabel, labelText, cancellationToken).ConfigureAwait(false);
                await manifestWriter.ExportImageAsync(sample.GalleryId, sample.FileName, assignment.Split,
                    sample.SourcePath, targetImage, cancellationToken, labelText: labelText).ConfigureAwait(false);

                if (assignment.Split == "train")
                {
                    trainingBoxes += sample.Boxes.Count;
                }
                else
                {
                    validationBoxes += sample.Boxes.Count;
                }
            }

            var manifest = await manifestWriter.WriteAsync(DatasetManifest.Detection, effectiveClasses,
                byPurpose ? "purpose-assignment" : DetectionSplitPlanner.Method,
                byPurpose ? null : ClassificationSplitPlanner.Seed, cancellationToken,
                trainingBoxes, validationBoxes).ConfigureAwait(false);
            var trainCount = manifest.TrainingCount!.Value;
            var valCount = manifest.ValidationCount!.Value;
            var fingerprint = manifest.DatasetFingerprint!;

            var result = new DetectionDatasetExportResult(
                outputRoot,
                total,
                trainCount,
                valCount,
                trainingBoxes,
                validationBoxes,
                skippedUnreviewed,
                skippedMissingImage,
                skippedBoxes,
                effectiveClasses,
                fingerprint);
            _logger.LogInformation(
                "Exported detection dataset: train={Train}, val={Val}, boxes={Boxes}, skippedUnreviewed={Unreviewed}, skippedBoxes={SkippedBoxes}, fingerprint={Fingerprint}",
                trainCount, valCount, trainingBoxes + validationBoxes, skippedUnreviewed, skippedBoxes, fingerprint);
            return Result<DetectionDatasetExportResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export detection dataset");
            return Result<DetectionDatasetExportResult>.Failure($"导出检测数据集失败：{ex.Message}");
        }
    }

    private static Result<List<SplitAssignment>> AssignAutomatically(
        IReadOnlyList<DetectionSample> samples,
        double validationRatio)
    {
        var split = DetectionSplitPlanner.AssignValidationByRank(
            samples.Select(sample => (sample.GalleryId, sample.FileName)).ToArray(),
            validationRatio);
        if (split.IsFailure || split.Value is null)
        {
            return Result<List<SplitAssignment>>.Failure(split.Error ?? "检测数据集拆分失败。");
        }

        var assignments = samples
            .Select((sample, index) => new SplitAssignment(
                sample,
                split.Value[index] ? "val" : "train"))
            .ToList();
        return ValidateAssignments(assignments);
    }

    private static Result<List<SplitAssignment>> AssignByPurpose(
        IReadOnlyList<DetectionSample> samples,
        IReadOnlyDictionary<Guid, GalleryDatasetRole> roles)
    {
        var assignments = new List<SplitAssignment>();
        foreach (var sample in samples)
        {
            if (!roles.TryGetValue(sample.GalleryId, out var role) || role == GalleryDatasetRole.None)
            {
                continue;
            }

            if (role is GalleryDatasetRole.Training or GalleryDatasetRole.TrainingAndValidation)
            {
                assignments.Add(new SplitAssignment(sample, "train"));
            }

            if (role is GalleryDatasetRole.Validation or GalleryDatasetRole.TrainingAndValidation)
            {
                assignments.Add(new SplitAssignment(sample, "val"));
            }
        }

        return ValidateAssignments(assignments);
    }

    private static Result<List<SplitAssignment>> ValidateAssignments(List<SplitAssignment> assignments)
    {
        if (!assignments.Any(item => item.Split == "train"))
        {
            return Result<List<SplitAssignment>>.Failure("训练集为空，请检查图库角色或验证集比例。");
        }

        if (!assignments.Any(item => item.Split == "val"))
        {
            return Result<List<SplitAssignment>>.Failure("验证集为空，请检查图库角色或验证集比例。");
        }

        return Result<List<SplitAssignment>>.Success(assignments);
    }

    private static (double Width, double Height) ReadImageSize(string imagePath)
    {
        try
        {
            using var mat = Cv2.ImRead(imagePath, ImreadModes.Unchanged);
            return mat.Empty() ? (0, 0) : (mat.Width, mat.Height);
        }
        catch
        {
            return (0, 0);
        }
    }

    private sealed record DetectionSample(
        Guid GalleryId,
        string FileName,
        string SourcePath,
        double ImageWidth,
        double ImageHeight,
        IReadOnlyList<(int ClassIndex, AnnotationShape Shape)> Boxes);

    private sealed record SplitAssignment(DetectionSample Sample, string Split);
}
