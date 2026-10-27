using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ClassificationDatasetExportService : IClassificationDatasetExportService
{
    private readonly IGalleryRepository _galleries;
    private readonly IProjectRepository _projects;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly IProjectFileStore _fileStore;
    private readonly ILogger<ClassificationDatasetExportService> _logger;

    public ClassificationDatasetExportService(
        IGalleryRepository galleries,
        IProjectRepository projects,
        IAnnotationWorkflowService annotations,
        IProjectFileStore fileStore,
        ILogger<ClassificationDatasetExportService> logger)
    {
        _galleries = galleries;
        _projects = projects;
        _annotations = annotations;
        _fileStore = fileStore;
        _logger = logger;
    }

    public async Task<Result<ClassificationDatasetExportResult>> ExportAsync(
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
                return Result<ClassificationDatasetExportResult>.Failure("项目不存在。");
            }
            // Dataset export is a read-only operation over the historical project data.
            // Archived projects remain exportable; only mutating/operational paths require
            // ProjectCapabilityCatalog.EnsureActive.

            var selectedIds = galleryIds.Where(id => id != Guid.Empty).Distinct().ToArray();
            if (selectedIds.Length == 0)
            {
                return Result<ClassificationDatasetExportResult>.Failure("请至少选择一个分类图库。");
            }

            var normalizedClasses = classes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var samples = new List<ClassifiedSample>();
            var total = 0;
            var missingImage = 0;
            var missingClass = 0;
            foreach (var galleryId in selectedIds)
            {
                var gallery = await _galleries.GetSummaryByIdAsync(galleryId, cancellationToken);
                if (gallery is null || gallery.ProjectId != projectId)
                {
                    return Result<ClassificationDatasetExportResult>.Failure("所选图库不存在或不属于当前项目。");
                }

                var fileNames = await _galleries.ListSampleFileNamesAsync(galleryId, cancellationToken: cancellationToken);
                total += fileNames.Count;
                foreach (var fileName in fileNames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var document = await _annotations.LoadAsync(galleryId, fileName, cancellationToken);
                    var imageClass = NormalizeClass(document.ImageClass, normalizedClasses);
                    if (imageClass is null)
                    {
                        missingClass++;
                        continue;
                    }

                    var source = _fileStore.ResolveSampleImagePath(projectId, galleryId, fileName);
                    if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    {
                        missingImage++;
                        continue;
                    }

                    samples.Add(new ClassifiedSample(galleryId, fileName, source, imageClass));
                }
            }

            if (samples.Count == 0)
            {
                return Result<ClassificationDatasetExportResult>.Failure("没有可用的图像级分类样本。");
            }

            var activeClassSet = samples
                .Select(s => s.ImageClass)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 优先保持项目类别定义的顺序，仅保留实际含有已标注样本的有效类别
            var effectiveClasses = normalizedClasses.Length > 0
                ? normalizedClasses.Where(activeClassSet.Contains).ToArray()
                : activeClassSet.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToArray();

            // 若样本中包含项目标签之外的类别（如外部导入），追加至末尾
            if (normalizedClasses.Length > 0)
            {
                var extraActive = activeClassSet
                    .Where(c => !effectiveClasses.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
                effectiveClasses = effectiveClasses.Concat(extraActive).ToArray();
            }

            if (effectiveClasses.Length < 2)
            {
                return Result<ClassificationDatasetExportResult>.Failure(
                    $"图像分类训练至少需要 2 个不同类别（当前样本仅包含 {effectiveClasses.Length} 个类别：{string.Join("、", effectiveClasses)}）。");
            }

            var classDirectories = effectiveClasses
                .Select((value, index) => (value, directory: index.ToString("D4")))
                .ToDictionary(item => item.value, item => item.directory, StringComparer.OrdinalIgnoreCase);

            var assignments = splitMode == TrainingDatasetSplitMode.PurposeAssignment
                ? AssignByPurpose(samples, galleryRoles, effectiveClasses)
                : AssignAutomatically(samples, validationRatio, effectiveClasses);
            if (assignments.IsFailure || assignments.Value is null)
            {
                return Result<ClassificationDatasetExportResult>.Failure(assignments.Error ?? "分类数据集拆分失败。");
            }

            DatasetOutputDirectoryGuard.PrepareEmpty(outputRoot);

            foreach (var split in new[] { "train", "val" })
            {
                foreach (var imageClass in effectiveClasses)
                {
                    Directory.CreateDirectory(Path.Combine(outputRoot, split, classDirectories[imageClass]));
                }
            }

            foreach (var assignment in assignments.Value)
            {
                var extension = Path.GetExtension(assignment.Sample.FileName);
                var safeName = $"{assignment.Sample.GalleryId:N}_{Path.GetFileNameWithoutExtension(assignment.Sample.FileName)}{extension}";
                var target = Path.Combine(
                    outputRoot,
                    assignment.Split,
                    classDirectories[assignment.Sample.ImageClass],
                    safeName);
                FastFileLinker.TryLinkOrCopy(assignment.Sample.SourcePath, target, overwrite: true);
            }

            var trainCount = assignments.Value.Count(item => item.Split == "train");
            var valCount = assignments.Value.Count(item => item.Split == "val");
            var fingerprint = ComputeFingerprint(assignments.Value);
            await File.WriteAllTextAsync(
                Path.Combine(outputRoot, "dataset-manifest.json"),
                JsonSerializer.Serialize(new
                {
                    schema_version = 1,
                    classes = effectiveClasses,
                    dataset_fingerprint = fingerprint,
                    training_count = trainCount,
                    validation_count = valCount,
                    // Recorded so a split can be reproduced or audited; purpose-based splits ignore it.
                    split_method = ClassificationSplitPlanner.Method,
                    split_seed = ClassificationSplitPlanner.Seed
                }, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            var result = new ClassificationDatasetExportResult(
                outputRoot,
                total,
                samples.Count,
                trainCount,
                valCount,
                missingImage,
                missingClass,
                effectiveClasses,
                fingerprint);
            _logger.LogInformation(
                "Exported classification dataset: train={Train}, val={Val}, missingClass={MissingClass}, missingImage={MissingImage}, fingerprint={Fingerprint}",
                trainCount, valCount, missingClass, missingImage, fingerprint);
            return Result<ClassificationDatasetExportResult>.Success(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export classification dataset");
            return Result<ClassificationDatasetExportResult>.Failure($"导出分类数据集失败：{ex.Message}");
        }
    }

    private static Result<List<SplitAssignment>> AssignByPurpose(
        IReadOnlyList<ClassifiedSample> samples,
        IReadOnlyDictionary<Guid, GalleryDatasetRole> roles,
        IReadOnlyList<string> classes)
    {
        var result = new List<SplitAssignment>();
        foreach (var sample in samples)
        {
            if (!classes.Contains(sample.ImageClass, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!roles.TryGetValue(sample.GalleryId, out var role) || role == GalleryDatasetRole.None)
            {
                continue;
            }

            if (role is GalleryDatasetRole.Training or GalleryDatasetRole.TrainingAndValidation)
            {
                result.Add(new SplitAssignment(sample, "train"));
            }
            if (role is GalleryDatasetRole.Validation or GalleryDatasetRole.TrainingAndValidation)
            {
                result.Add(new SplitAssignment(sample, "val"));
            }
        }
        return ValidateAssignments(result, classes);
    }

    private static Result<List<SplitAssignment>> AssignAutomatically(
        IReadOnlyList<ClassifiedSample> samples,
        double validationRatio,
        IReadOnlyList<string> classes)
    {
        var included = samples
            .Where(sample => classes.Contains(sample.ImageClass, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var split = ClassificationSplitPlanner.AssignValidation(
            included
                .Select(sample => new ClassificationSplitCandidate(sample.GalleryId, sample.FileName, sample.ImageClass))
                .ToArray(),
            validationRatio);
        if (split.IsFailure || split.Value is null)
        {
            return Result<List<SplitAssignment>>.Failure(split.Error ?? "分类数据集拆分失败。");
        }

        var result = included
            .Select((sample, index) => new SplitAssignment(sample, split.Value[index] ? "val" : "train"))
            .ToList();
        return ValidateAssignments(result, classes);
    }

    private static Result<List<SplitAssignment>> ValidateAssignments(
        List<SplitAssignment> assignments,
        IReadOnlyList<string> classes)
    {
        foreach (var split in new[] { "train", "val" })
        {
            foreach (var imageClass in classes)
            {
                if (!assignments.Any(item => item.Split == split
                    && string.Equals(item.Sample.ImageClass, imageClass, StringComparison.OrdinalIgnoreCase)))
                {
                    return Result<List<SplitAssignment>>.Failure($"{split} 集缺少 {imageClass} 类样本。");
                }
            }
        }
        return Result<List<SplitAssignment>>.Success(assignments);
    }

    private static string ComputeFingerprint(IEnumerable<SplitAssignment> assignments)
    {
        var text = string.Join("\n", assignments
            .OrderBy(item => item.Split, StringComparer.Ordinal)
            .ThenBy(item => item.Sample.ImageClass, StringComparer.Ordinal)
            .ThenBy(item => item.Sample.GalleryId)
            .ThenBy(item => item.Sample.FileName, StringComparer.Ordinal)
            .Select(item => $"{item.Split}|{item.Sample.ImageClass}|{item.Sample.GalleryId:N}|{item.Sample.FileName}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string? NormalizeClass(string? value, IReadOnlyList<string> classes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (classes.Count == 0)
        {
            return trimmed;
        }

        return classes.FirstOrDefault(candidate =>
            string.Equals(candidate, trimmed, StringComparison.OrdinalIgnoreCase)) ?? trimmed;
    }

    private sealed record ClassifiedSample(Guid GalleryId, string FileName, string SourcePath, string ImageClass);
    private sealed record SplitAssignment(ClassifiedSample Sample, string Split);
}
