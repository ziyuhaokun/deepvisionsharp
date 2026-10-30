using System.Text.Json;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Saves YOLO26 detection checkpoints and their sidecars. Unlike classification a detection
/// model may have a single class, and the class list can be recovered from the exported
/// <c>detection-dataset/dataset-manifest.json</c> when a cancelled run lost its sidecar.
/// </summary>
internal sealed class DetectionModelArtifactHandler : IModelArtifactHandler
{
    private readonly IProjectFileStore _projectFileStore;
    private readonly ModelMetadataService _modelMetadataService;

    public DetectionModelArtifactHandler(
        IProjectFileStore projectFileStore,
        ModelMetadataService modelMetadataService)
    {
        _projectFileStore = projectFileStore;
        _modelMetadataService = modelMetadataService;
    }

    public string HandlerKey => ArtifactHandlerKeys.Detection;

    public bool CanHandle(ModelArtifactContext context)
        => ModelTypeNames.IsDetectionModelType(context.ModelType);

    public Result<string> ResolveBestCheckpointPath(ModelArtifactContext context)
        => ResolveCheckpointPath(context with { CheckpointSelection = TrainingCheckpointSelection.Best });

    public Result<string> ResolveCheckpointPath(ModelArtifactContext context)
    {
        return context.CheckpointSelection switch
        {
            TrainingCheckpointSelection.Last => ResolveLast(context),
            _ => ResolveBest(context)
        };
    }

    private static Result<string> ResolveBest(ModelArtifactContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.BestModelPath) && File.Exists(context.BestModelPath))
        {
            return IsSupportedCheckpoint(context.BestModelPath)
                ? Result<string>.Success(context.BestModelPath)
                : Result<string>.Failure($"训练产物不是支持的 checkpoint：{context.BestModelPath}");
        }

        var fromDisk = TrainingCheckpointResolver.ResolveBestFromOutputDirectory(context.OutputRoot);
        return !string.IsNullOrWhiteSpace(fromDisk) && IsSupportedCheckpoint(fromDisk)
            ? Result<string>.Success(fromDisk)
            : Result<string>.Failure("找不到最佳训练产物（best.torchsharp）");
    }

    private static Result<string> ResolveLast(ModelArtifactContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.LastModelPath) && File.Exists(context.LastModelPath))
        {
            return IsSupportedCheckpoint(context.LastModelPath)
                ? Result<string>.Success(context.LastModelPath)
                : Result<string>.Failure($"训练产物不是支持的 checkpoint：{context.LastModelPath}");
        }

        var fromDisk = TrainingCheckpointResolver.ResolveLastFromOutputDirectory(context.OutputRoot);
        return !string.IsNullOrWhiteSpace(fromDisk) && IsSupportedCheckpoint(fromDisk)
            ? Result<string>.Success(fromDisk)
            : Result<string>.Failure("找不到末轮训练产物（last.torchsharp）");
    }

    public Task<Result<IReadOnlyList<string>>> RecoverClassesAsync(
        ModelArtifactContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.SourceCheckpointPath)
            || !File.Exists(context.SourceCheckpointPath))
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Failure("训练产物 checkpoint 文件不存在"));
        }

        var metadata = _modelMetadataService.ReadMetadata(context.SourceCheckpointPath);
        if (Validate(metadata, context.ModelType).IsSuccess && metadata is { Classes.Count: > 0 })
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Success(metadata.Classes));
        }

        var fromDataset = TryReadClassesFromManifest(context.DataRoot)
            ?? TryReadClassesFromManifest(context.OutputRoot);
        return Task.FromResult(fromDataset is { Count: > 0 }
            ? Result<IReadOnlyList<string>>.Success(fromDataset)
            : Result<IReadOnlyList<string>>.Failure(
                "训练中断后缺少检测 sidecar，且无法从 detection-dataset/dataset-manifest.json 解析类别。"));
    }

    public async Task<Result<SavedTrainedModelArtifact>> SaveAsync(
        ModelArtifactContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.SourceCheckpointPath) || !File.Exists(context.SourceCheckpointPath))
        {
            return Result<SavedTrainedModelArtifact>.Failure("训练产物 checkpoint 文件不存在");
        }

        var classes = context.CandidateClasses
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToList();
        if (classes.Count == 0)
        {
            return Result<SavedTrainedModelArtifact>.Failure("模型类别为空，无法保存模型产物");
        }

        var metadata = _modelMetadataService.ReadMetadata(context.SourceCheckpointPath);
        var validation = Validate(metadata, context.ModelType);
        if (validation.IsFailure || metadata is null)
        {
            return Result<SavedTrainedModelArtifact>.Failure(validation.Error ?? "模型缺少可读取的 .info sidecar");
        }

        if (!classes.SequenceEqual(metadata.Classes, StringComparer.Ordinal))
        {
            return Result<SavedTrainedModelArtifact>.Failure("训练任务类别与模型 sidecar class map 不一致");
        }

        var modelsDirectory = _projectFileStore.GetModelsDirectory(context.ProjectId, ensureExists: true);
        var extension = Path.GetExtension(context.SourceCheckpointPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".torchsharp";
        }

        var destination = ModelArtifactPaths.ResolveDestinationPath(
            modelsDirectory, context.ModelName, extension, DateTime.Now);
        var completed = false;
        try
        {
            await Task.Run(
                () => File.Copy(context.SourceCheckpointPath, destination, overwrite: true),
                cancellationToken).ConfigureAwait(false);
            var metadataPath = await ModelArtifactPaths
                .CopySidecarIfExistsAsync(context.SourceCheckpointPath, destination, cancellationToken)
                .ConfigureAwait(false);
            if (metadataPath is null || !File.Exists(metadataPath))
            {
                return Result<SavedTrainedModelArtifact>.Failure("训练产物缺少完整 sidecar，已拒绝保存模型");
            }

            completed = true;
            return Result<SavedTrainedModelArtifact>.Success(
                new SavedTrainedModelArtifact(destination, metadataPath));
        }
        finally
        {
            if (!completed)
            {
                TryDelete(destination);
                TryDelete(destination + ".info");
            }
        }
    }

    private static Result Validate(ModelMetadata? metadata, string expectedModelType)
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

        if (!ModelTypeNames.IsDetectionModelType(expectedModelType))
        {
            return Result.Failure("仅支持检测模型");
        }

        return Result.Success();
    }

    private static List<string>? TryReadClassesFromManifest(string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        var path = Path.Combine(root, "detection-dataset", "dataset-manifest.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("classes", out var classes)
                || classes.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var names = classes.EnumerateArray()
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!.Trim())
                .ToList();
            return names.Count > 0 ? names : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsSupportedCheckpoint(string checkpointPath)
    {
        var extension = Path.GetExtension(checkpointPath);
        return extension.Equals(".torchsharp", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".torchscript", StringComparison.OrdinalIgnoreCase)
               || extension.Equals(".pt", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
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
            // Best-effort cleanup of a partial artifact.
        }
    }
}
