using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class TrainedModelArtifactService : ITrainedModelArtifactService
{
    private readonly ModelArtifactHandlerRegistry _registry;
    private readonly ILogger<TrainedModelArtifactService> _logger;
    private readonly SqliteDb _sqliteDb;
    private readonly IProjectFileStore _fileStore;

    public TrainedModelArtifactService(
        ModelArtifactHandlerRegistry registry,
        SqliteDb sqliteDb,
        IProjectFileStore fileStore,
        ILogger<TrainedModelArtifactService> logger)
    {
        _registry = registry;
        _sqliteDb = sqliteDb;
        _fileStore = fileStore;
        _logger = logger;
    }

    public Result<string> ResolveBestCheckpointPath(ModelArtifactCheckpointRequest request)
        => ResolveCheckpointPath(request with { Selection = TrainingCheckpointSelection.Best });

    public Result<string> ResolveCheckpointPath(ModelArtifactCheckpointRequest request)
    {
        var context = BuildResolveContext(request);
        var handler = _registry.Resolve(context);
        return handler.IsFailure || handler.Value is null
            ? Result<string>.Failure(handler.Error ?? $"不支持的模型产物类型：{request.ModelType}")
            : handler.Value.ResolveCheckpointPath(context);
    }

    public async Task<Result<IReadOnlyList<string>>> RecoverClassesAsync(
        ModelArtifactClassRecoveryRequest request,
        CancellationToken cancellationToken)
    {
        var context = new ModelArtifactContext(
            ProjectId: Guid.Empty,
            ModelName: string.Empty,
            ModelType: request.ModelType,
            BestModelPath: null,
            LastModelPath: null,
            OutputRoot: request.OutputRoot,
            DataRoot: request.DataRoot,
            SourceCheckpointPath: request.CheckpointPath,
            CandidateClasses: request.CandidateClasses ?? Array.Empty<string>(),
            Form: null);

        var handler = _registry.Resolve(context);
        return handler.IsFailure || handler.Value is null
            ? Result<IReadOnlyList<string>>.Failure(handler.Error ?? $"不支持的模型产物类型：{request.ModelType}")
            : await handler.Value.RecoverClassesAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<SavedTrainedModelArtifact>> SaveModelArtifactAsync(
        ModelArtifactSaveRequest request,
        CancellationToken cancellationToken)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_sqliteDb.Instance, request.ProjectId, cancellationToken).ConfigureAwait(false);
        var context = new ModelArtifactContext(
            request.ProjectId,
            request.ModelName,
            request.ModelType,
            BestModelPath: request.BestModelPath,
            LastModelPath: request.LastModelPath,
            OutputRoot: request.OutputRoot,
            DataRoot: request.DataRoot,
            SourceCheckpointPath: request.SourceCheckpointPath,
            CandidateClasses: request.Classes,
            Form: request.Form,
            CheckpointSelection: request.CheckpointSelection,
            TrainingJobId: request.TrainingJobId);

        var handler = _registry.Resolve(context);
        return handler.IsFailure || handler.Value is null
            ? Result<SavedTrainedModelArtifact>.Failure(handler.Error ?? $"不支持的模型产物类型：{request.ModelType}")
            : await handler.Value.SaveAsync(context, cancellationToken).ConfigureAwait(false);
    }

    public Result<string> TryRenameCheckpointFiles(string checkpointPath, string newModelName)
    {
        SqlProjectExecutionGuard.EnsurePathWritable(_sqliteDb.Instance, _fileStore, checkpointPath);
        if (string.IsNullOrWhiteSpace(checkpointPath))
        {
            return Result<string>.Failure("模型文件路径无效");
        }

        var directory = Path.GetDirectoryName(checkpointPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return Result<string>.Failure("模型文件路径无效");
        }

        var extension = Path.GetExtension(checkpointPath);
        var safeName = ModelArtifactPaths.SanitizeSegment(newModelName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "model";
        }

        var desiredPath = Path.Combine(directory, $"{safeName}{extension}");
        var currentFullPath = Path.GetFullPath(checkpointPath);
        var desiredFullPath = Path.GetFullPath(desiredPath);
        if (string.Equals(currentFullPath, desiredFullPath, StringComparison.OrdinalIgnoreCase))
        {
            return Result<string>.Success(checkpointPath);
        }

        desiredPath = ModelArtifactPaths.EnsureUniquePath(desiredPath);

        var oldInfoPath = checkpointPath + ".info";
        var newInfoPath = desiredPath + ".info";
        var movedCheckpoint = false;
        var movedSidecar = false;
        try
        {
            File.Move(checkpointPath, desiredPath);
            movedCheckpoint = true;

            if (File.Exists(oldInfoPath))
            {
                if (File.Exists(newInfoPath))
                {
                    File.Delete(newInfoPath);
                }

                File.Move(oldInfoPath, newInfoPath);
                movedSidecar = true;
            }

            return Result<string>.Success(desiredPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename checkpoint from {OldPath} to {NewPath}", checkpointPath, desiredPath);

            // Roll back so the pair stays consistent. A checkpoint without its sidecar (or the
            // reverse) is unusable: the loader needs both and reports "missing metadata" forever.
            if (movedSidecar)
            {
                try
                {
                    File.Move(newInfoPath, oldInfoPath);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(
                        rollbackEx,
                        "Could not restore sidecar {NewInfo} to {OldInfo}; model pair is inconsistent",
                        newInfoPath,
                        oldInfoPath);
                }
            }

            if (movedCheckpoint)
            {
                try
                {
                    File.Move(desiredPath, checkpointPath);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogError(
                        rollbackEx,
                        "Could not restore checkpoint {NewPath} to {OldPath}; model pair is inconsistent",
                        desiredPath,
                        checkpointPath);
                }
            }

            return Result<string>.Failure($"重命名模型文件失败：{ex.Message}");
        }
    }

    private static ModelArtifactContext BuildResolveContext(ModelArtifactCheckpointRequest request)
        => new(
            ProjectId: Guid.Empty,
            ModelName: string.Empty,
            ModelType: request.ModelType,
            BestModelPath: request.BestModelPath,
            LastModelPath: request.LastModelPath,
            OutputRoot: request.OutputRoot,
            DataRoot: null,
            SourceCheckpointPath: null,
            CandidateClasses: Array.Empty<string>(),
            Form: null,
            CheckpointSelection: request.Selection);
}
