using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class TrainingJobLifecycleService : ITrainingJobLifecycleService, IDisposable
{
    private readonly ITrainingJobRepository _repository;
    private readonly IProjectRepository _projects;
    private readonly TrainingBackendRegistry _backendRegistry;
    private readonly TrainingJobStartPreparer _startPreparer;
    private readonly TrainingJobRecoveryCoordinator _recoveryCoordinator;
    private readonly TrainingJobStatusWriter _statusWriter;
    private readonly ILogger<TrainingJobLifecycleService> _logger;
    private readonly ProjectOperationGate _operations;
    private readonly TrainingRunQueue _runQueue;
    /// <summary>Job ids whose start is currently in flight; used as an atomic single-flight marker.</summary>
    private readonly ConcurrentDictionary<Guid, byte> _startingJobs = new();

    public TrainingJobLifecycleService(
        ITrainingJobRepository repository,
        IProjectRepository projects,
        TrainingBackendRegistry backendRegistry,
        TrainingJobStartPreparer startPreparer,
        TrainingJobRecoveryCoordinator recoveryCoordinator,
        TrainingJobStatusWriter statusWriter,
        ILogger<TrainingJobLifecycleService> logger,
        ProjectOperationGate operations,
        TrainingRunQueue runQueue)
    {
        _repository = repository;
        _projects = projects;
        _backendRegistry = backendRegistry;
        _startPreparer = startPreparer;
        _recoveryCoordinator = recoveryCoordinator;
        _statusWriter = statusWriter;
        _logger = logger;
        _operations = operations;
        _runQueue = runQueue;
        _statusWriter.JobStatusChanged += RaiseStatusChanged;
    }

    public bool HasActiveRun => _runQueue.HasAny;

    public event Action<Guid, TrainingJobStatus, string?>? JobStatusChanged;

    public async Task<Result<StartTrainingJobResult>> StartAsync(
        StartTrainingJobCommand request,
        CancellationToken cancellationToken = default)
    {
        // The gate lease keeps project deletion blocked for the whole run, so ownership moves to
        // the queued run once the job is prepared (and is disposed here on any earlier failure).
        var operation = _operations.EnterStart(request.ProjectId);
        var leaseTransferred = false;
        try
        {
            var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken).ConfigureAwait(false);
            if (project is null)
            {
                return Result<StartTrainingJobResult>.Failure("项目不存在或已被删除。");
            }

            ProjectCapabilityCatalog.EnsureActive(project.Type);
            var existing = await _repository.GetByIdAsync(request.JobId, cancellationToken).ConfigureAwait(false);
            if (existing?.ProjectId is Guid owningProjectId && owningProjectId != request.ProjectId)
            {
                return Result<StartTrainingJobResult>.Failure("训练任务不属于当前项目。");
            }

            // Atomic single-flight flag per job: TryAdd wins exactly one caller and the entry is
            // removed when the start finishes, so the set never grows with the number of jobs.
            if (!_startingJobs.TryAdd(request.JobId, 0))
            {
                return Result<StartTrainingJobResult>.Failure("训练正在启动中，请稍候");
            }

            try
            {
                var result = await StartCoreAsync(request, operation, cancellationToken).ConfigureAwait(false);
                leaseTransferred = result.IsSuccess;
                return result;
            }
            finally
            {
                _startingJobs.TryRemove(request.JobId, out _);
            }
        }
        finally
        {
            if (!leaseTransferred)
            {
                operation.Dispose();
            }
        }
    }

    public async Task<Result> StopAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (job is null)
        {
            return Result.Failure($"训练任务 {jobId} 不存在");
        }

        if (job.ProjectId is Guid projectId)
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (project is null)
            {
                return Result.Failure("项目不存在或已被删除。");
            }

            ProjectCapabilityCatalog.EnsureActive(project.Type);
        }

        switch (job.Status)
        {
            case TrainingJobStatus.Draft:
                return Result.Failure("任务未在运行");
            case TrainingJobStatus.Succeeded:
            case TrainingJobStatus.Failed:
            case TrainingJobStatus.Cancelled:
                return Result.Failure("任务已结束");
        }

        // The queue owns the run token, so one cancel covers queued, exporting and training phases.
        _runQueue.TryCancel(jobId);

        if (job.Status == TrainingJobStatus.Preparing)
        {
            await _statusWriter.MarkCancelledAsync(job, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }

        var backend = ResolveTrainingBackend(job);
        if (backend.IsFailure || backend.Value is null)
        {
            return Result.Failure(backend.Error ?? "训练后端不存在");
        }

        return await backend.Value
            .StopAsync(job, "User requested stop", cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<Guid>> DuplicateAsDraftAsync(
        Guid sourceJobId,
        Guid? targetProjectId = null,
        CancellationToken cancellationToken = default)
    {
        var source = await _repository.GetByIdAsync(sourceJobId, cancellationToken).ConfigureAwait(false);
        if (source is null)
        {
            return Result<Guid>.Failure($"训练任务 {sourceJobId} 不存在");
        }

        if (source.ProjectId is Guid sourceProjectId)
        {
            var project = await _projects.GetByIdAsync(sourceProjectId, cancellationToken).ConfigureAwait(false);
            if (project is null)
            {
                return Result<Guid>.Failure("项目不存在或已被删除。");
            }

            ProjectCapabilityCatalog.EnsureActive(project.Type);
        }

        if (source.Status is not (TrainingJobStatus.Succeeded or TrainingJobStatus.Failed or TrainingJobStatus.Cancelled))
        {
            return Result<Guid>.Failure("仅已完成、失败或已取消的任务可复制为新任务");
        }

        var projectId = targetProjectId ?? source.ProjectId;
        var baseName = source.Name.Trim();
        var candidateName = baseName;
        var peers = projectId is Guid pid
            ? await _repository.GetByProjectIdAsync(pid, cancellationToken).ConfigureAwait(false)
            : await _repository.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (peers.Any(job => job.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase)))
        {
            for (var index = 2; index < 10_000; index++)
            {
                candidateName = $"{baseName} ({index})";
                if (!peers.Any(job => job.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }
            }
        }

        var draft = TrainingJob.CreateDraft(candidateName, projectId);
        var copyResult = draft.CopySettingsFrom(source);
        if (copyResult.IsFailure || copyResult.Value is null)
        {
            return Result<Guid>.Failure(copyResult.Error ?? "无法复制训练任务配置");
        }

        await _repository.SaveAsync(copyResult.Value, cancellationToken).ConfigureAwait(false);
        return Result<Guid>.Success(copyResult.Value.Id);
    }

    public Task<Result<int>> ReconcileOrphanedJobsAsync(CancellationToken cancellationToken = default)
        => _recoveryCoordinator.ReconcileOrphanedJobsAsync(
            cancellationToken,
            // A queued or executing run is Preparing/Running without a process yet; the reconciler
            // must not mark it failed while the worker is still exporting the dataset.
            isStartInFlight: _runQueue.IsTracked);

    private async Task<Result<StartTrainingJobResult>> StartCoreAsync(
        StartTrainingJobCommand request,
        IDisposable gateLease,
        CancellationToken cancellationToken)
    {
        // Check the queue before preparing. PrepareAsync marks the job Preparing and clears the
        // inference model cache; discovering "another run is already active" only at enqueue time
        // made the job flash Preparing then fail, and needlessly dropped resident models.
        if (_runQueue.HasAny)
        {
            return Result<StartTrainingJobResult>.Failure(
                "已有训练任务正在运行，请等待其完成或停止后再启动。");
        }

        // Catalog maps modelType → TrainingBackendKey; CanHandle validates after key lookup.
        var backend = _backendRegistry.Resolve(request.Form.ModelType);
        if (backend.IsFailure || backend.Value is null)
        {
            return Result<StartTrainingJobResult>.Failure(backend.Error ?? "训练后端不存在");
        }

        TrainingStartContext context;
        try
        {
            var prepareResult = await _startPreparer
                .PrepareAsync(request, backend.Value, cancellationToken)
                .ConfigureAwait(false);
            if (prepareResult.IsFailure || prepareResult.Value is null)
            {
                var error = prepareResult.Error ?? "无法启动训练任务";
                await PersistStartFailureAsync(request.JobId, error, cancellationToken).ConfigureAwait(false);
                return Result<StartTrainingJobResult>.Failure(error);
            }

            context = prepareResult.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller gave up before the run was handed over; nothing was queued.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to prepare training job {JobId}", request.JobId);
            await PersistStartFailureAsync(request.JobId, ex.Message, CancellationToken.None).ConfigureAwait(false);
            return Result<StartTrainingJobResult>.Failure(ex.Message);
        }

        // From here the worker owns the run: dataset export, training and the terminal status.
        if (!_runQueue.TryEnqueue(
                new TrainingRunRequest(request.JobId, request.ProjectId, context, backend.Value, gateLease),
                out var enqueueError))
        {
            var error = enqueueError ?? "无法启动训练任务";
            await PersistStartFailureAsync(request.JobId, error, CancellationToken.None).ConfigureAwait(false);
            return Result<StartTrainingJobResult>.Failure(error);
        }

        // Snapshot: the backend keeps appending run-time messages to the context list.
        return Result<StartTrainingJobResult>.Success(new StartTrainingJobResult(
            context.Job.ModelType,
            context.WorkspacePlan.VocRoot,
            context.WorkspacePlan.OutputRoot,
            context.WorkspacePlan.LogFile,
            DateTime.UtcNow,
            context.InformationalMessages.ToArray()));
    }

    private void RaiseStatusChanged(Guid jobId, TrainingJobStatus status, string? bestModelPath)
    {
        JobStatusChanged?.Invoke(jobId, status, bestModelPath);
    }

    private async Task PersistStartFailureAsync(
        Guid jobId,
        string failureMessage,
        CancellationToken cancellationToken)
    {
        var latest = await _repository.GetByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (latest?.ProjectId is Guid projectId)
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken).ConfigureAwait(false);
            if (project is null || !ProjectCapabilityCatalog.IsActive(project.Type)) return;
        }
        if (latest?.Status is TrainingJobStatus.Preparing or TrainingJobStatus.Failed)
        {
            await _statusWriter
                .MarkFailedAsync(
                    latest,
                    cancellationToken,
                    failureMessage,
                    notifyStatusChanged: latest.Status != TrainingJobStatus.Failed)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Prefer job.Backend (authoritative key written at configure time); fall back to catalog via modelType.
    /// </summary>
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

    public void Dispose()
    {
        _statusWriter.JobStatusChanged -= RaiseStatusChanged;
        _startingJobs.Clear();
    }
}

