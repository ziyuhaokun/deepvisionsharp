using System.Diagnostics;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Application.Models;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class TrainingJobRecoveryCoordinator
{
    private readonly ITrainingJobRepository _repository;
    private readonly IProjectRepository _projects;
    private readonly ITaskCapabilityResolver _capabilities;
    private readonly TrainingBackendRegistry _backendRegistry;
    private readonly TrainingJobStatusWriter _statusWriter;

    private readonly ILogger<TrainingJobRecoveryCoordinator> _logger;

    public TrainingJobRecoveryCoordinator(
        ITrainingJobRepository repository,
        IProjectRepository projects,
        ITaskCapabilityResolver capabilities,
        TrainingBackendRegistry backendRegistry,
        TrainingJobStatusWriter statusWriter,
        ILogger<TrainingJobRecoveryCoordinator> logger)
    {
        _repository = repository;
        _projects = projects;
        _capabilities = capabilities;
        _backendRegistry = backendRegistry;
        _statusWriter = statusWriter;
        _logger = logger;
    }

    public async Task<Result<int>> ReconcileOrphanedJobsAsync(
        CancellationToken cancellationToken = default,
        Func<Guid, bool>? isStartInFlight = null)
    {
        var jobs = await _repository
            .GetByStatusesAsync([TrainingJobStatus.Preparing, TrainingJobStatus.Running], cancellationToken)
            .ConfigureAwait(false);

        var fixedCount = 0;
        foreach (var job in jobs)
        {
            // A job whose project or model is no longer supported can never be resumed, and no
            // process can be running it (this coordinator only sees jobs left active by a previous
            // process). Leaving it active would strand it in Preparing/Running forever: the UI
            // refuses to delete an active job and Stop finds no run token to cancel.
            if (job.ProjectId is Guid projectId)
            {
                var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
                if (project is null || !ProjectCapabilityCatalog.IsActive(project.Type))
                {
                    await MarkUnrecoverableAsync(
                        job,
                        project is null ? "所属项目已不存在，训练无法恢复" : "所属项目类型已停用，训练无法恢复",
                        cancellationToken).ConfigureAwait(false);
                    fixedCount++;
                    continue;
                }
            }

            if (_capabilities.ValidateModelExecution(job.ModelType).IsFailure)
            {
                await MarkUnrecoverableAsync(
                    job,
                    $"模型类型 {job.ModelType} 已不受支持，训练无法恢复",
                    cancellationToken).ConfigureAwait(false);
                fixedCount++;
                continue;
            }

            // Preparing has no process yet; EvaluateRecovery would MarkFailed while StartAsync
            // is still exporting / spawning. In-memory MarkRunning then overwrites Failed → UI flash.
            if (isStartInFlight?.Invoke(job.Id) == true)
            {
                _logger.LogDebug(
                    "Skipping reconcile for training job {JobId}: start still in flight",
                    job.Id);
                continue;
            }

            var backendResult = ResolveTrainingBackend(job);
            if (backendResult.IsFailure || backendResult.Value is null)
            {
                TrainingRunMarkerStore.Delete(job.OutputRoot);
                await _statusWriter
                    .MarkFailedAsync(job, cancellationToken, backendResult.Error ?? "训练后端不存在")
                    .ConfigureAwait(false);
                fixedCount++;
                continue;
            }

            var decision = await backendResult.Value
                .EvaluateRecoveryAsync(job, cancellationToken)
                .ConfigureAwait(false);

            switch (decision.Action)
            {
                case TrainingRecoveryAction.KeepRunning:
                    continue;
                case TrainingRecoveryAction.MarkSucceeded:
                    var completeResult = await _statusWriter
                        .CompleteAsync(
                            job,
                            TrainingJobStatus.Succeeded,
                            decision.BestModelPath,
                            decision.LastModelPath,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (completeResult.IsFailure)
                    {
                        _logger.LogWarning(
                            "Unable to recover orphaned training job {JobId} as succeeded: {Error}",
                            job.Id,
                            completeResult.Error);
                        await _statusWriter
                            .MarkFailedAsync(job, cancellationToken, decision.Reason)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogInformation("Recovered orphaned training job {JobId} as succeeded", job.Id);
                    }

                    fixedCount++;
                    break;
                case TrainingRecoveryAction.MarkCancelled:
                    await _statusWriter.MarkCancelledAsync(job, cancellationToken).ConfigureAwait(false);
                    fixedCount++;
                    break;
                case TrainingRecoveryAction.MarkFailed:
                    TryTerminateRecoveredOrphanProcess(job);
                    await _statusWriter.MarkFailedAsync(job, cancellationToken, decision.Reason).ConfigureAwait(false);
                    fixedCount++;
                    break;
            }
        }

        if (fixedCount > 0)
        {
            _logger.LogWarning("Reconciled {Count} orphaned training job(s)", fixedCount);
        }

        return Result<int>.Success(fixedCount);
    }

    /// <summary>
    /// Moves a job that can never run again to a terminal state so it stops being treated as active.
    /// </summary>
    private async Task MarkUnrecoverableAsync(TrainingJob job, string reason, CancellationToken cancellationToken)
    {
        if (!TrainingJobTransitions.IsActive(job.Status))
        {
            return;
        }

        _logger.LogWarning(
            "Training job {JobId} cannot be recovered ({Reason}); marking it failed",
            job.Id,
            reason);
        await _statusWriter.MarkFailedAsync(job, cancellationToken, reason).ConfigureAwait(false);
    }

    private Result<ITrainingBackend> ResolveTrainingBackend(TrainingJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.Backend))
        {
            var byKey = _backendRegistry.ResolveByKey(job.Backend);
            if (byKey.IsFailure || byKey.Value is null)
            {
                return byKey;
            }

            if (!byKey.Value.CanHandle(job.ModelType))
            {
                return Result<ITrainingBackend>.Failure(
                    $"训练后端 {job.Backend} 不支持模型类型：{job.ModelType}");
            }

            return byKey;
        }

        return _backendRegistry.Resolve(job.ModelType);
    }

    private void TryTerminateRecoveredOrphanProcess(TrainingJob job)
    {
        if (job.ProcessId is not int processId || processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            _logger.LogWarning(
                "Killed orphaned training process tree for job {JobId}, pid={ProcessId}",
                job.Id,
                processId);
        }
        catch (ArgumentException)
        {
            // Process already exited before recovery cleanup.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to kill orphaned training process tree for job {JobId}, pid={ProcessId}",
                job.Id,
                processId);
        }
    }
}

