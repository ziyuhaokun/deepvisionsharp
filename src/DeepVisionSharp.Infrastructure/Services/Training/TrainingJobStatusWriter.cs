using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class TrainingJobStatusWriter
{
    private readonly ITrainingJobRepository _repository;
    private readonly ILogger<TrainingJobStatusWriter> _logger;

    public TrainingJobStatusWriter(
        ITrainingJobRepository repository,
        ILogger<TrainingJobStatusWriter> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public event Action<Guid, TrainingJobStatus, string?>? JobStatusChanged;

    public async Task<Result<TrainingJob>> MarkPreparingAsync(
        TrainingJob job,
        Guid projectId,
        string name,
        TrainingConfiguration configuration,
        TrainingWorkspacePlan plan,
        CancellationToken cancellationToken)
    {
        var result = job.PrepareForStart(
            projectId,
            name,
            configuration,
            plan.VocRoot,
            plan.OutputRoot,
            plan.LogFile);

        if (result.IsFailure || result.Value is null)
        {
            return Result<TrainingJob>.Failure(result.Error ?? "无法启动训练任务");
        }

        await _repository.SaveAsync(result.Value, cancellationToken).ConfigureAwait(false);
        RaiseStatusChanged(result.Value.Id, TrainingJobStatus.Preparing, null);
        return Result<TrainingJob>.Success(result.Value);
    }

    public async Task<Result<TrainingJob>> MarkPreparingForResumeAsync(
        TrainingJob job,
        CancellationToken cancellationToken)
    {
        var result = job.PrepareForResume();
        if (result.IsFailure || result.Value is null)
        {
            return Result<TrainingJob>.Failure(result.Error ?? "无法进入续训准备");
        }

        await _repository.SaveAsync(result.Value, cancellationToken).ConfigureAwait(false);
        RaiseStatusChanged(result.Value.Id, TrainingJobStatus.Preparing, null);
        return Result<TrainingJob>.Success(result.Value);
    }

    public async Task<Result<TrainingJob>> MarkRunningAsync(
        TrainingJob job,
        int processId,
        CancellationToken cancellationToken,
        string? dataRoot = null)
    {
        var result = job.MarkRunning(processId, DateTime.UtcNow, dataRoot);
        if (result.IsFailure || result.Value is null)
        {
            return Result<TrainingJob>.Failure(result.Error ?? "无法进入运行状态");
        }

        await _repository.SaveAsync(result.Value, cancellationToken).ConfigureAwait(false);
        RaiseStatusChanged(result.Value.Id, TrainingJobStatus.Running, null);
        return Result<TrainingJob>.Success(result.Value);
    }

    public async Task<Result<TrainingJob>> CompleteAsync(
        TrainingJob job,
        TrainingJobStatus status,
        string? bestModelPath,
        string? lastModelPath = null,
        CancellationToken cancellationToken = default,
        string? failureMessage = null,
        bool captureCheckpointPaths = true)
    {
        var result = job.Complete(
            status,
            DateTime.UtcNow,
            bestModelPath,
            lastModelPath,
            captureCheckpointPaths: captureCheckpointPaths,
            failureMessage: failureMessage);
        if (result.IsFailure || result.Value is null)
        {
            return Result<TrainingJob>.Failure(result.Error ?? "无法完成任务状态更新");
        }

        await SaveWithRetryAsync(result.Value, cancellationToken: cancellationToken).ConfigureAwait(false);
        RaiseStatusChanged(result.Value.Id, status, result.Value.BestModelPath);
        return Result<TrainingJob>.Success(result.Value);
    }

    public async Task MarkFailedAsync(
        TrainingJob job,
        CancellationToken cancellationToken = default,
        string? failureMessage = null,
        bool notifyStatusChanged = true)
    {
        if (!string.IsNullOrWhiteSpace(failureMessage))
        {
            _logger.LogWarning("Training job {JobId} failed: {Reason}", job.Id, failureMessage);
        }

        // Keep usable weights from an interrupted run so "save as model" still works. Mirrors
        // MarkCancelledAsync: classification writes best.torchsharp at the run root, and the
        // shell's save gate falls back to a .pt-only probe when BestModelPath is empty.
        var best = TrainingJobRecoveryEvaluator.TryResolveBestModelPath(job);
        var last = TrainingJobRecoveryEvaluator.TryResolveLastModelPath(job);
        var result = best is not null || last is not null
            ? job.Complete(
                TrainingJobStatus.Failed,
                DateTime.UtcNow,
                bestModelPath: best,
                lastModelPath: last,
                captureCheckpointPaths: true,
                failureMessage: failureMessage)
            : job.MarkFailed(DateTime.UtcNow, failureMessage);
        if (result.IsFailure || result.Value is null)
        {
            _logger.LogWarning("Unable to mark training job {JobId} as failed: {Error}", job.Id, result.Error);
            return;
        }

        await SaveWithRetryAsync(result.Value, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (notifyStatusChanged)
        {
            RaiseStatusChanged(result.Value.Id, TrainingJobStatus.Failed, result.Value.BestModelPath);
        }
    }

    public async Task MarkCancelledAsync(
        TrainingJob job,
        CancellationToken cancellationToken = default)
    {
        var best = TrainingJobRecoveryEvaluator.TryResolveBestModelPath(job);
        var last = TrainingJobRecoveryEvaluator.TryResolveLastModelPath(job);
        var result = best is not null || last is not null
            ? job.Complete(
                TrainingJobStatus.Cancelled,
                DateTime.UtcNow,
                bestModelPath: best,
                lastModelPath: last,
                captureCheckpointPaths: true)
            : job.MarkCancelled(DateTime.UtcNow);
        if (result.IsFailure || result.Value is null)
        {
            _logger.LogWarning("Unable to mark training job {JobId} as cancelled: {Error}", job.Id, result.Error);
            return;
        }

        await SaveWithRetryAsync(result.Value, cancellationToken: cancellationToken).ConfigureAwait(false);
        RaiseStatusChanged(result.Value.Id, TrainingJobStatus.Cancelled, result.Value.BestModelPath);
    }

    private async Task SaveWithRetryAsync(
        TrainingJob job,
        int attempts = 3,
        CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await _repository.SaveAsync(job, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < attempts)
            {
                _logger.LogWarning(ex, "Retry {Attempt} saving training job {JobId}", attempt, job.Id);
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        await _repository.SaveAsync(job, cancellationToken).ConfigureAwait(false);
    }

    private void RaiseStatusChanged(Guid jobId, TrainingJobStatus status, string? bestModelPath)
    {
        JobStatusChanged?.Invoke(jobId, status, bestModelPath);
    }
}
