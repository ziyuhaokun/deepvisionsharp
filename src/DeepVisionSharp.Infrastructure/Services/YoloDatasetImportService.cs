using Microsoft.Extensions.Logging;
using OpenCvSharp;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.Datasets;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Imports a YOLO dataset by staging each image next to a generated Labelme sidecar and
/// handing the batch to the existing gallery importer, so project gating, name conflicts,
/// rollback, label index and statistics all keep their single implementation.
/// </summary>
internal sealed class YoloDatasetImportService : IYoloDatasetImportService
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp" };

    private static readonly string[] ClassFileNames =
        ["classes.txt", "data.yaml", "data.yml", "dataset.yaml", "dataset.yml"];

    private readonly IGalleryRepository _galleries;
    private readonly IProjectRepository _projects;
    private readonly IProjectLabelService _labels;
    private readonly IGalleryTransferService _galleryTransfer;
    private readonly ILogger<YoloDatasetImportService> _logger;

    public YoloDatasetImportService(
        IGalleryRepository galleries,
        IProjectRepository projects,
        IProjectLabelService labels,
        IGalleryTransferService galleryTransfer,
        ILogger<YoloDatasetImportService> logger)
    {
        _galleries = galleries;
        _projects = projects;
        _labels = labels;
        _galleryTransfer = galleryTransfer;
        _logger = logger;
    }

    public async Task<YoloDatasetImportResult> ImportAsync(
        YoloDatasetImportRequest request,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        if (!Directory.Exists(request.DatasetRoot))
        {
            errors.Add($"数据集目录不存在：{request.DatasetRoot}");
            return Failed(0, errors, warnings, Array.Empty<string>());
        }

        var gallery = await _galleries.GetSummaryByIdAsync(request.GalleryId, cancellationToken).ConfigureAwait(false);
        if (gallery is null)
        {
            errors.Add("图库不存在。");
            return Failed(0, errors, warnings, Array.Empty<string>());
        }

        var project = await _projects.GetByIdAsync(gallery.ProjectId, cancellationToken).ConfigureAwait(false);
        if (project is null)
        {
            errors.Add("项目不存在。");
            return Failed(0, errors, warnings, Array.Empty<string>());
        }

        if (!ProjectTypes.IsDetection(project.Type))
        {
            errors.Add("YOLO 数据集只能导入目标检测项目。");
            return Failed(0, errors, warnings, Array.Empty<string>());
        }

        var classesResult = LoadClasses(request.DatasetRoot);
        if (classesResult.IsFailure || classesResult.Value is null)
        {
            errors.Add(classesResult.Error ?? "无法解析数据集类别。");
            return Failed(0, errors, warnings, Array.Empty<string>());
        }

        var classes = classesResult.Value;
        var images = EnumerateImages(request.DatasetRoot).ToArray();
        if (images.Length == 0)
        {
            errors.Add("数据集中没有可导入的图像。");
            return Failed(0, errors, warnings, classes);
        }

        await EnsureProjectLabelsAsync(gallery.ProjectId, classes, cancellationToken).ConfigureAwait(false);

        var stagingRoot = Path.Combine(Path.GetTempPath(), "dvs-yolo-import", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingRoot);
        var stagedImages = new List<string>();
        var boxCount = 0;
        try
        {
            progress?.Report(new GalleryTransferProgress("正在准备 YOLO 数据集", 0, images.Length, 0));
            for (var index = 0; index < images.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imagePath = images[index];
                var stagedName = $"{index:D6}_{Path.GetFileName(imagePath)}";
                var stagedImagePath = Path.Combine(stagingRoot, stagedName);

                var (width, height) = ReadImageSize(imagePath);
                if (width <= 0 || height <= 0)
                {
                    warnings.Add($"{Path.GetFileName(imagePath)}：无法读取图像尺寸，按未标注导入。");
                    FastFileLinker.TryLinkOrCopy(imagePath, stagedImagePath);
                    stagedImages.Add(stagedImagePath);
                    progress?.Report(new GalleryTransferProgress(
                        "正在准备 YOLO 数据集", index + 1, images.Length, stagedImages.Count, Path.GetFileName(imagePath)));
                    continue;
                }

                var labelPath = ResolveLabelPath(request.DatasetRoot, imagePath);
                YoloBox[] labels = [];
                if (labelPath is not null)
                {
                    var labelText = await File.ReadAllTextAsync(labelPath, cancellationToken).ConfigureAwait(false);
                    labels = YoloDatasetFormat.ParseLabels(labelText, classes.Count, out var parseWarnings).ToArray();
                    warnings.AddRange(parseWarnings.Select(message => $"{Path.GetFileName(labelPath)}：{message}"));
                }

                var shapes = new List<AnnotationShape>();
                foreach (var box in labels)
                {
                    var shape = YoloDatasetFormat.ToShape(box, classes[box.ClassIndex], width, height);
                    if (shape is not null)
                    {
                        shapes.Add(shape);
                    }
                }

                var fileName = Path.GetFileName(stagedImagePath);
                var document = new LabelmeDocument
                {
                    Version = "5.0.1",
                    ImagePath = fileName,
                    ImageData = null,
                    ImageWidth = width,
                    ImageHeight = height,
                    Flags = LabelmeAnnotationSerializer.BuildDocumentFlags(imageClass: null, reviewed: true),
                    Shapes = LabelmeAnnotationSerializer.ToLabelmeShapes(shapes)
                };
                var documentJson = LabelmeAnnotationSerializer.Serialize(document);

                FastFileLinker.TryLinkOrCopy(imagePath, stagedImagePath);
                await File.WriteAllTextAsync(
                    SampleAnnotationFiles.GetPath(stagedImagePath),
                    documentJson,
                    cancellationToken).ConfigureAwait(false);

                stagedImages.Add(stagedImagePath);
                boxCount += shapes.Count;
                progress?.Report(new GalleryTransferProgress(
                    "正在准备 YOLO 数据集", index + 1, images.Length, stagedImages.Count, Path.GetFileName(imagePath)));
            }

            var transfer = await _galleryTransfer
                .ImportSamplesFromFilesAsync(request.GalleryId, stagedImages, progress, cancellationToken)
                .ConfigureAwait(false);

            errors.AddRange(transfer.Errors);
            _logger.LogInformation(
                "Imported YOLO dataset into gallery {GalleryId}: images={Total}, added={Added}, boxes={Boxes}, classes={Classes}",
                request.GalleryId,
                transfer.Total,
                transfer.Added,
                boxCount,
                classes.Count);

            return new YoloDatasetImportResult(
                transfer.Succeeded && transfer.Added > 0,
                transfer.Total,
                transfer.Added,
                transfer.Failed,
                transfer.Skipped,
                boxCount,
                classes,
                errors,
                warnings);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            TryDeleteDirectory(stagingRoot);
        }
    }

    private static Result<IReadOnlyList<string>> LoadClasses(string root)
    {
        foreach (var name in ClassFileNames)
        {
            var path = Path.Combine(root, name);
            if (!File.Exists(path))
            {
                continue;
            }

            var result = YoloDatasetFormat.ParseClasses(File.ReadAllText(path));
            if (result.IsSuccess)
            {
                return result;
            }
        }

        return Result<IReadOnlyList<string>>.Failure(
            "未找到可解析的类别文件（classes.txt 或 data.yaml 的 names）。");
    }

    private static IEnumerable<string> EnumerateImages(string root)
    {
        var imagesRoot = Path.Combine(root, "images");
        var start = Directory.Exists(imagesRoot) ? imagesRoot : root;
        foreach (var path in Directory.EnumerateFiles(start, "*.*", SearchOption.AllDirectories))
        {
            if (!ImageExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            if (IsUnderLabelsDirectory(path, root))
            {
                continue;
            }

            yield return path;
        }
    }

    private static bool IsUnderLabelsDirectory(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => string.Equals(segment, "labels", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveLabelPath(string root, string imagePath)
    {
        var relative = Path.GetRelativePath(root, imagePath);
        var segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var imagesIndex = Array.FindIndex(segments, segment => string.Equals(segment, "images", StringComparison.OrdinalIgnoreCase));
        if (imagesIndex >= 0)
        {
            var mirrored = segments.ToArray();
            mirrored[imagesIndex] = "labels";
            mirrored[^1] = Path.ChangeExtension(mirrored[^1], ".txt");
            var candidate = Path.Combine(root, Path.Combine(mirrored));
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        var sibling = Path.ChangeExtension(imagePath, ".txt");
        return File.Exists(sibling) ? sibling : null;
    }

    private static (int Width, int Height) ReadImageSize(string imagePath)
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

    private async Task EnsureProjectLabelsAsync(
        Guid projectId,
        IReadOnlyList<string> classes,
        CancellationToken cancellationToken)
    {
        var existing = await _labels.ListAsync(projectId, cancellationToken).ConfigureAwait(false);
        var names = existing.Select(label => label.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = classes
            .Where(name => !names.Contains(name))
            .Select(name => new ProjectLabelSeed(name, "#3B82F6"))
            .ToArray();
        if (missing.Length > 0)
        {
            await _labels.AddManyAsync(projectId, missing, cancellationToken).ConfigureAwait(false);
        }
    }

    private static YoloDatasetImportResult Failed(
        int total,
        IReadOnlyList<string> errors,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> classes) =>
        new(false, total, 0, 0, 0, 0, classes, errors, warnings);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Staging leftovers live under the system temp directory and are safe to leave behind.
        }
    }
}
