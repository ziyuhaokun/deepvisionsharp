using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Models;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ClassificationModelArtifactHandler : IModelArtifactHandler
{
    private readonly IProjectFileStore _projectFileStore;
    private readonly ModelMetadataService _modelMetadataService;

    public ClassificationModelArtifactHandler(
        IProjectFileStore projectFileStore,
        ModelMetadataService modelMetadataService)
    {
        _projectFileStore = projectFileStore;
        _modelMetadataService = modelMetadataService;
    }

    public string HandlerKey => ArtifactHandlerKeys.Classification;

    public bool CanHandle(ModelArtifactContext context)
        => ModelTypeNames.IsClassificationModelType(context.ModelType);

    public Result<string> ResolveBestCheckpointPath(ModelArtifactContext context)
        => ResolveCheckpointPath(context with { CheckpointSelection = TrainingCheckpointSelection.Best });

    public Result<string> ResolveCheckpointPath(ModelArtifactContext context)
    {
        return context.CheckpointSelection switch
        {
            TrainingCheckpointSelection.Last => ResolveLastCheckpointPath(context),
            _ => ResolveBestCheckpointPathInternal(context)
        };
    }

    private Result<string> ResolveBestCheckpointPathInternal(ModelArtifactContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.BestModelPath) && File.Exists(context.BestModelPath))
        {
            return IsSupportedCheckpoint(context.BestModelPath)
                ? Result<string>.Success(context.BestModelPath)
                : Result<string>.Failure($"训练产物不是支持的 checkpoint：{context.BestModelPath}");
        }

        var fromDisk = TrainingCheckpointResolver.ResolveBestFromOutputDirectory(context.OutputRoot);
        if (!string.IsNullOrWhiteSpace(fromDisk) && IsSupportedCheckpoint(fromDisk))
        {
            return Result<string>.Success(fromDisk);
        }

        return Result<string>.Failure("找不到最佳训练产物（best.pt / best.torchsharp）");
    }

    private Result<string> ResolveLastCheckpointPath(ModelArtifactContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.LastModelPath) && File.Exists(context.LastModelPath))
        {
            return IsSupportedCheckpoint(context.LastModelPath)
                ? Result<string>.Success(context.LastModelPath)
                : Result<string>.Failure($"训练产物不是支持的 checkpoint：{context.LastModelPath}");
        }

        var fromDisk = TrainingCheckpointResolver.ResolveLastFromOutputDirectory(context.OutputRoot);
        if (!string.IsNullOrWhiteSpace(fromDisk) && IsSupportedCheckpoint(fromDisk))
        {
            return Result<string>.Success(fromDisk);
        }

        return Result<string>.Failure("找不到末轮训练产物（last.pt / last.torchsharp）");
    }

    public async Task<Result<IReadOnlyList<string>>> RecoverClassesAsync(
        ModelArtifactContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.SourceCheckpointPath)
            || !File.Exists(context.SourceCheckpointPath))
        {
            return Result<IReadOnlyList<string>>.Failure("训练产物 checkpoint 文件不存在");
        }

        var metadata = _modelMetadataService.ReadMetadata(context.SourceCheckpointPath);
        var validation = ValidateTrainedMetadata(metadata, context.ModelType);
        if (validation.IsSuccess && metadata is not null && metadata.Classes.Count > 0)
        {
            await Task.CompletedTask;
            return Result<IReadOnlyList<string>>.Success(metadata.Classes);
        }

        // Cancelled runs often have best/last.pt without a sidecar .info — recover classes from
        // the training dataset layout under OutputRoot (source-voc / *-dataset), not only job.DataRoot.
        var fromData = TryReadClassesFromDatasetRoots(context.DataRoot, context.OutputRoot);
        if (fromData is { Count: > 0 })
        {
            await Task.CompletedTask;
            return Result<IReadOnlyList<string>>.Success(fromData);
        }

        return Result<IReadOnlyList<string>>.Failure(
            "训练中断后缺少 .info sidecar，且无法从数据集目录解析类别。"
            + " 请确认输出目录下仍有 source-voc、classification-dataset、segmentation-dataset 或 semantic-dataset。");
    }

    public async Task<Result<SavedTrainedModelArtifact>> SaveAsync(
        ModelArtifactContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.SourceCheckpointPath) || !File.Exists(context.SourceCheckpointPath))
        {
            return Result<SavedTrainedModelArtifact>.Failure("训练产物 checkpoint 文件不存在");
        }

        var classes = NormalizeClasses(context.CandidateClasses);
        if (classes.Count == 0)
        {
            return Result<SavedTrainedModelArtifact>.Failure("模型类别为空，无法保存模型产物");
        }

        var sourceMetadata = _modelMetadataService.ReadMetadata(context.SourceCheckpointPath);

        var metadataValidation = ValidateTrainedMetadata(sourceMetadata, context.ModelType);
        var hasValidSidecar = metadataValidation.IsSuccess && sourceMetadata is not null;

        if (hasValidSidecar
            && !classes.SequenceEqual(sourceMetadata!.Classes, StringComparer.Ordinal))
        {
            return Result<SavedTrainedModelArtifact>.Failure("训练任务类别与模型 sidecar class map 不一致");
        }

        if (!hasValidSidecar)
        {
            var synthesized = TrySynthesizeTrainedMetadata(context, classes);
            if (synthesized.IsFailure || synthesized.Value is null)
            {
                return Result<SavedTrainedModelArtifact>.Failure(
                    synthesized.Error
                    ?? metadataValidation.Error
                    ?? "训练产物缺少 sidecar，且无法从任务配置合成元数据");
            }

            sourceMetadata = synthesized.Value;
        }

        var modelsDir = _projectFileStore.GetModelsDirectory(context.ProjectId, ensureExists: true);
        var checkpointExtension = Path.GetExtension(context.SourceCheckpointPath);
        if (string.IsNullOrWhiteSpace(checkpointExtension))
        {
            checkpointExtension = ".pt";
        }

        var destCheckpointPath = ModelArtifactPaths.ResolveDestinationPath(
            modelsDir,
            context.ModelName,
            checkpointExtension,
            DateTime.Now);

        var completed = false;
        try
        {
            await Task.Run(
                () => File.Copy(context.SourceCheckpointPath, destCheckpointPath, overwrite: true),
                cancellationToken);

            string? metadataPath;
            if (hasValidSidecar)
            {
                metadataPath = await ModelArtifactPaths
                    .CopySidecarIfExistsAsync(context.SourceCheckpointPath, destCheckpointPath, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _modelMetadataService
                    .WriteMetadataAsync(destCheckpointPath, sourceMetadata!, cancellationToken)
                    .ConfigureAwait(false);
                metadataPath = destCheckpointPath + ".info";
            }

            if (metadataPath is null || !File.Exists(metadataPath))
            {
                return Result<SavedTrainedModelArtifact>.Failure("训练产物缺少完整 sidecar，已拒绝保存模型");
            }

            completed = true;
            return Result<SavedTrainedModelArtifact>.Success(new SavedTrainedModelArtifact(
                destCheckpointPath,
                metadataPath));
        }
        finally
        {
            // Any exit before the pair is complete — cancellation, IO failure, or the missing-sidecar
            // rejection above — would leave a weights file with no metadata and no database row. The
            // models folder is later picked up by path, so that orphan reports "missing metadata"
            // forever.
            if (!completed)
            {
                CleanupPartialArtifact(destCheckpointPath);
            }
        }
    }

    private static bool IsSupportedCheckpoint(string checkpointPath)
    {
        var ext = Path.GetExtension(checkpointPath);
        return ext.Equals(".pt", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".torchsharp", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".torchscript", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".ckpt", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> NormalizeClasses(IEnumerable<string>? values)
        => values?
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList() ?? new List<string>();

    private static Result ValidateTrainedMetadata(ModelMetadata? metadata, string expectedModelType)
    {
        if (metadata is null)
        {
            return Result.Failure("模型缺少可读取的 .info sidecar");
        }

        if (metadata.SchemaVersion != 2)
        {
            return Result.Failure($"不支持的 sidecar schema：{metadata.SchemaVersion}");
        }

        if (!string.Equals(metadata.ArtifactKind, "trained_model", StringComparison.Ordinal))
        {
            return Result.Failure($"artifact kind 不是 trained_model：{metadata.ArtifactKind}");
        }

        if (!string.Equals(metadata.ModelType, expectedModelType, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("sidecar model_type 与训练任务不一致");
        }

        if (metadata.Classes.Count == 0 || string.IsNullOrWhiteSpace(metadata.DatasetFingerprint))
        {
            return Result.Failure("sidecar 缺少 classes 或 dataset_fingerprint");
        }

        if (!ModelTypeNames.IsClassificationModelType(expectedModelType))
        {
            return Result.Failure("仅支持分类模型");
        }

        return Result.Success();
    }

    private static List<string>? TryReadClassesFromDatasetRoots(
        string? dataRoot,
        string? outputRoot)
    {
        foreach (var root in EnumerateDatasetRoots(dataRoot, outputRoot))
        {
            var classes = TryReadClassesFromDataRoot(root);
            if (classes is { Count: > 0 })
            {
                return classes;
            }
        }

        return null;
    }

    private static List<string>? TryReadClassesFromDataRoot(string? dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot) || !Directory.Exists(dataRoot))
        {
            return null;
        }

        // Try every common classification export class source (order is intentional).
        var fromManifest = TryReadClassesFromManifest(dataRoot);
        if (fromManifest is { Count: > 0 })
        {
            return fromManifest;
        }

        var fromLabelsTxt = TryReadPlainLines(Path.Combine(dataRoot, "labels.txt"));
        if (fromLabelsTxt is { Count: > 0 })
        {
            return fromLabelsTxt;
        }

        var fromClassesTxt = TryReadClassesTxt(dataRoot);
        if (fromClassesTxt is { Count: > 0 })
        {
            return fromClassesTxt;
        }

        return null;
    }

    private static List<string>? TryReadPlainLines(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var labels = File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToList();
            return labels.Count > 0 ? labels : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? TryReadClassesFromManifest(string dataRoot)
    {
        var manifestPath = Path.Combine(dataRoot, "dataset-manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("classes", out var classesElement)
                || classesElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return null;
            }

            var classes = new List<string>();
            foreach (var item in classesElement.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        classes.Add(value.Trim());
                    }
                }
                else if (item.ValueKind == System.Text.Json.JsonValueKind.Object
                         && item.TryGetProperty("name", out var nameEl)
                         && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var value = nameEl.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        classes.Add(value.Trim());
                    }
                }
            }

            return classes.Count > 0 ? classes : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? TryReadClassesTxt(string dataRoot)
    {
        var classesPath = Path.Combine(dataRoot, "classes.txt");
        if (!File.Exists(classesPath))
        {
            return null;
        }

        try
        {
            var classes = new List<string>();
            foreach (var line in File.ReadAllLines(classesPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                {
                    continue;
                }

                // Formats: "name" | "trainId\tname\tlabelId" | "name color"
                var parts = trimmed.Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                string name;
                if (parts.Length >= 2 && int.TryParse(parts[0], out _))
                {
                    name = parts[1];
                }
                else
                {
                    name = parts[0];
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    classes.Add(name);
                }
            }

            return classes.Count > 0 ? classes : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Known training export roots across all product workflows.
    /// job.DataRoot is often still plan.VocRoot (.../voc) even when export lives under
    /// source-voc / classification-dataset / segmentation-dataset / semantic-dataset.
    /// </summary>
    private static readonly string[] KnownDatasetSubfolders =
    [
        "source-voc",
        "classification-dataset",
        "segmentation-dataset",
        "semantic-dataset",
        "voc",
        "contracts"
    ];

    private static IEnumerable<string> EnumerateDatasetRoots(string? dataRoot, string? outputRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full))
                {
                    ordered.Add(full);
                }
            }
            catch
            {
                // ignore invalid paths
            }
        }

        void AddKnownUnder(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            foreach (var name in KnownDatasetSubfolders)
            {
                Add(Path.Combine(root, name));
            }
        }

        Add(dataRoot);
        Add(outputRoot);
        AddKnownUnder(outputRoot);

        if (!string.IsNullOrWhiteSpace(dataRoot))
        {
            try
            {
                var trimmed = dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var parent = Path.GetDirectoryName(trimmed);
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    // When DataRoot is .../voc, siblings hold the real exports.
                    AddKnownUnder(parent);
                }
            }
            catch
            {
                // ignore
            }
        }

        return ordered;
    }

    private static string ResolveDatasetFingerprint(string? dataRoot, string? outputRoot)
    {
        foreach (var root in EnumerateDatasetRoots(dataRoot, outputRoot))
        {
            var manifestPath = Path.Combine(root, "dataset-manifest.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (document.RootElement.TryGetProperty("dataset_fingerprint", out var fingerprint)
                    && fingerprint.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var value = fingerprint.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value.Trim();
                    }
                }
            }
            catch
            {
                // fall through
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Best-effort removal of a half-written model artifact (weights plus sidecar).
    /// </summary>
    private void CleanupPartialArtifact(string checkpointPath)
    {
        foreach (var path in new[] { checkpointPath, checkpointPath + ".info" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: the file stays, but the save already reported failure.
            }
        }
    }

    private Result<ModelMetadata> TrySynthesizeTrainedMetadata(
        ModelArtifactContext context,
        IReadOnlyList<string> classes)
    {
        var form = context.Form;
        if (form is null)
        {
            return Result<ModelMetadata>.Failure("缺少训练任务配置，无法为取消产物合成 sidecar");
        }

        var fingerprint = ResolveDatasetFingerprint(context.DataRoot, context.OutputRoot);

        // The architecture and preprocessing are not optional: the engine rebuilds the module from
        // `arch` before loading weights, and reads `preprocessing` to normalise inputs. Omitting them
        // makes a resnet34 checkpoint fail to load and silently mispredicts for ImageNet-pretrained
        // models, so derive them the same way the training backend did at start.
        var archName = TorchSharpClassifierFactory.ResolveArch(context.ModelType);
        var preprocessing = PretrainedWeightsLocator.Resolve(archName) is null
            ? PreprocessingVersion.UnitScaleV1
            : PreprocessingVersion.ImageNetV2;
        var weightsFormat = string.Equals(
            Path.GetExtension(context.SourceCheckpointPath),
            ".torchsharp",
            StringComparison.OrdinalIgnoreCase)
            ? "torchsharp"
            : "torchscript";

        var metadata = new ModelMetadata
        {
            SchemaVersion = 2,
            ArtifactKind = "trained_model",
            ModelType = context.ModelType,
            Classes = classes.ToList(),
            NumClasses = classes.Count,
            MaxDetections = 1,
            InputSize = Math.Max(1, Math.Max(form.NetworkInputWidth, form.NetworkInputHeight)),
            InputWidth = Math.Max(1, form.NetworkInputWidth),
            InputHeight = Math.Max(1, form.NetworkInputHeight),
            InputChannels = 3,
            DatasetFingerprint = fingerprint,
            Epochs = form.Epochs,
            BatchSize = form.BatchSize,
            Lr = form.Lr,
            Optimizer = form.Optimizer,
            Arch = archName,
            Preprocessing = preprocessing.ToWireValue(),
            WeightsFormat = weightsFormat,
            BestMap = 0,
            BestMap50 = 0,
            CreatedAt = DateTime.Now.ToString("O"),
            Version = "1.0",
            Notes = "从已取消训练的权重入库；可能未完成全部 epoch，请复核指标后再用于生产。",
            IsInitial = false
        };

        var validation = ValidateTrainedMetadata(metadata, context.ModelType);
        return validation.IsFailure
            ? Result<ModelMetadata>.Failure(validation.Error ?? "合成 sidecar 未通过校验")
            : Result<ModelMetadata>.Success(metadata);
    }
}
