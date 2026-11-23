using System.Text.Json;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.TorchSharp.Training;

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

    public Task<Result<IReadOnlyList<string>>> RecoverClassesAsync(
        ModelArtifactContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(context.SourceCheckpointPath) || !File.Exists(context.SourceCheckpointPath))
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Failure("训练产物 checkpoint 文件不存在"));
        }

        var metadata = _modelMetadataService.ReadMetadata(context.SourceCheckpointPath);
        if (ValidateTrainedMetadata(metadata, context.ModelType).IsSuccess)
        {
            return Task.FromResult(Result<IReadOnlyList<string>>.Success(metadata!.Classes));
        }

        var run = ReadMatchingRun(context, context.SourceCheckpointPath);
        var result = run.IsSuccess && run.Value is not null
            ? Result<IReadOnlyList<string>>.Success(run.Value.Classes)
            : Result<IReadOnlyList<string>>.Failure(run.Error!);
        return Task.FromResult(result);
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

        Dictionary<string, object?>? recoveredMetadata = null;
        if (hasValidSidecar)
        {
            if (!classes.SequenceEqual(sourceMetadata!.Classes, StringComparer.Ordinal))
            {
                return Result<SavedTrainedModelArtifact>.Failure("训练任务类别与模型 sidecar class map 不一致");
            }
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
                return Result<SavedTrainedModelArtifact>.Failure("训练任务类别与分类训练记录不一致");
            }

            recoveredMetadata = run.Value.CreateCheckpointMetadata();
            recoveredMetadata["notes"] = "按原始训练记录恢复元数据。该 checkpoint 的轮次和验证指标不可用。";
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
                    .WriteMetadataAsync(destCheckpointPath, recoveredMetadata!, cancellationToken)
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

        if (!MatchesModelType(metadata.ModelType, expectedModelType))
        {
            return Result.Failure("sidecar model_type 与训练任务不一致");
        }

        if (metadata.Classes is not { Count: > 0 } || string.IsNullOrWhiteSpace(metadata.DatasetFingerprint))
        {
            return Result.Failure("sidecar 缺少 classes 或 dataset_fingerprint");
        }

        if (!ModelTypeNames.IsClassificationModelType(expectedModelType))
        {
            return Result.Failure("仅支持分类模型");
        }

        return Result.Success();
    }

    private static Result<ClassificationTrainingRun> ReadMatchingRun(ModelArtifactContext context, string checkpointPath)
    {
        if (string.IsNullOrWhiteSpace(context.OutputRoot)
            || !string.Equals(Path.GetDirectoryName(Path.GetFullPath(checkpointPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(context.OutputRoot)), StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(checkpointPath) is not (TorchSharpClsTrainer.BestFileName or TorchSharpClsTrainer.LastFileName))
        {
            return Result<ClassificationTrainingRun>.Failure("模型缺少可用的 .info，且 checkpoint 不在原分类训练输出目录中。");
        }

        try
        {
            var run = ClassificationTrainingRun.Read(context.OutputRoot);
            if (run.TrainingJobId != context.TrainingJobId || !MatchesModelType(run.ModelType, context.ModelType))
            {
                return Result<ClassificationTrainingRun>.Failure("分类训练记录与当前任务或模型类型不一致，无法恢复模型元数据。");
            }

            return Result<ClassificationTrainingRun>.Success(run);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return Result<ClassificationTrainingRun>.Failure(
                $"模型缺少可用的 .info，且无法读取原分类训练记录 {ClassificationTrainingRun.FileName}。请恢复原始元数据或重新训练。{ex.Message}");
        }
    }

    private static bool MatchesModelType(string actual, string expected) =>
        ModelTypeNames.TryNormalize(actual, out var normalizedActual)
        && ModelTypeNames.TryNormalize(expected, out var normalizedExpected)
        && normalizedActual == normalizedExpected;

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

}
