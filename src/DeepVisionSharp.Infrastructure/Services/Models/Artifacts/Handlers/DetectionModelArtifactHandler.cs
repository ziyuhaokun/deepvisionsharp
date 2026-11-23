using System.Text.Json;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Detection.Training;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Saves YOLO26 detection checkpoints and recovers missing metadata from the original training run.
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
        cancellationToken.ThrowIfCancellationRequested();
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

        var run = ReadMatchingRun(context, context.SourceCheckpointPath);
        return Task.FromResult(run.IsSuccess && run.Value is not null
            ? Result<IReadOnlyList<string>>.Success(run.Value.Classes)
            : Result<IReadOnlyList<string>>.Failure(run.Error!));
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
        var hasValidSidecar = Validate(metadata, context.ModelType).IsSuccess;
        Dictionary<string, object?>? recoveredMetadata = null;
        int maxDetections;
        if (hasValidSidecar)
        {
            if (!classes.SequenceEqual(metadata!.Classes, StringComparer.Ordinal))
            {
                return Result<SavedTrainedModelArtifact>.Failure("训练任务类别与模型 sidecar class map 不一致");
            }

            maxDetections = metadata.MaxDetections;
        }
        else
        {
            var run = ReadMatchingRun(context, context.SourceCheckpointPath);
            if (run.IsFailure || run.Value is null)
            {
                return Result<SavedTrainedModelArtifact>.Failure(run.Error!);
            }

            if (!classes.SequenceEqual(run.Value.Classes, StringComparer.Ordinal))
            {
                return Result<SavedTrainedModelArtifact>.Failure("训练任务类别与检测训练记录不一致");
            }

            maxDetections = run.Value.MaxDetections;
            recoveredMetadata = run.Value.CreateCheckpointMetadata();
            recoveredMetadata["notes"] = "按原始训练记录恢复元数据。该 checkpoint 的轮次和验证指标不可用。";
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
            string? metadataPath;
            if (hasValidSidecar)
            {
                metadataPath = await ModelArtifactPaths
                    .CopySidecarIfExistsAsync(context.SourceCheckpointPath, destination, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await _modelMetadataService.WriteMetadataAsync(destination, recoveredMetadata!, cancellationToken).ConfigureAwait(false);
                metadataPath = destination + ".info";
            }

            if (metadataPath is null || !File.Exists(metadataPath))
            {
                return Result<SavedTrainedModelArtifact>.Failure("训练产物缺少完整 sidecar，已拒绝保存模型");
            }

            completed = true;
            return Result<SavedTrainedModelArtifact>.Success(
                new SavedTrainedModelArtifact(destination, metadataPath)
                {
                    MaxDetections = maxDetections > 0 ? maxDetections : null
                });
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

        if (!MatchesModelType(metadata.ModelType, expectedModelType))
        {
            return Result.Failure("sidecar model_type 与训练任务不一致");
        }

        if (metadata.Classes is not { Count: > 0 } || string.IsNullOrWhiteSpace(metadata.DatasetFingerprint))
        {
            return Result.Failure("sidecar 缺少 classes 或 dataset_fingerprint");
        }

        if (metadata.InputSize <= 0 && metadata.InputWidth <= 0)
        {
            return Result.Failure("sidecar 缺少检测模型输入尺寸");
        }

        if (!ModelTypeNames.IsDetectionModelType(expectedModelType))
        {
            return Result.Failure("仅支持检测模型");
        }

        return Result.Success();
    }

    private static Result<DetectionTrainingRun> ReadMatchingRun(ModelArtifactContext context, string checkpointPath)
    {
        if (string.IsNullOrWhiteSpace(context.OutputRoot)
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(checkpointPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.OutputRoot)), StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(checkpointPath) is not (TorchSharpDetTrainer.BestFileName or TorchSharpDetTrainer.LastFileName))
        {
            return Result<DetectionTrainingRun>.Failure("模型缺少可用的 .info，且 checkpoint 不在原检测训练输出目录中。");
        }

        try
        {
            var run = DetectionTrainingRun.Read(context.OutputRoot);
            if (run.TrainingJobId != context.TrainingJobId || !MatchesModelType(run.ModelType, context.ModelType))
            {
                return Result<DetectionTrainingRun>.Failure("检测训练记录与当前任务或模型类型不一致，无法恢复模型元数据。");
            }

            return Result<DetectionTrainingRun>.Success(run);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return Result<DetectionTrainingRun>.Failure(
                $"模型缺少可用的 .info，且无法读取原检测训练记录 {DetectionTrainingRun.FileName}。请恢复原始元数据或重新训练。{ex.Message}");
        }
    }

    private static bool MatchesModelType(string actual, string expected) =>
        ModelTypeNames.TryNormalize(actual, out var normalizedActual)
        && ModelTypeNames.TryNormalize(expected, out var normalizedExpected)
        && normalizedActual == normalizedExpected;

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
