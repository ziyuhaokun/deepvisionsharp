using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Training;

public sealed record DeleteTrainingJobCommand(Guid JobId) : ICommand<DeleteTrainingJobResult>;

/// <summary>
/// Outcome of deleting a training job row and optionally its OutputRoot.
/// </summary>
public sealed record DeleteTrainingJobResult(
    bool RunDirectoryDeleted,
    bool RunDirectoryAlreadyMissing);

public sealed class DeleteTrainingJobCommandHandler
    : ICommandHandler<DeleteTrainingJobCommand, DeleteTrainingJobResult>
{
    private readonly ITrainingJobRepository _repository;
    private readonly ITrainingProcessHost _processHost;
    private readonly ITrainingRunCleanupService _runCleanup;

    public DeleteTrainingJobCommandHandler(
        ITrainingJobRepository repository,
        ITrainingProcessHost processHost,
        ITrainingRunCleanupService runCleanup)
    {
        _repository = repository;
        _processHost = processHost;
        _runCleanup = runCleanup;
    }

    public async Task<Result<DeleteTrainingJobResult>> Handle(
        DeleteTrainingJobCommand request,
        CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null)
        {
            return Result<DeleteTrainingJobResult>.Failure($"训练任务 {request.JobId} 不存在");
        }

        // Block while preparing/running OR while the process host still tracks the job.
        // Preparing writes OutputRoot before the host registers a process; AND was a race.
        if (TrainingJobTransitions.IsActive(job.Status) || _processHost.IsActive(request.JobId))
        {
            return Result<DeleteTrainingJobResult>.Failure("请先停止正在运行的训练任务");
        }

        var runDirectoryDeleted = false;
        var runDirectoryAlreadyMissing = false;

        if (!string.IsNullOrWhiteSpace(job.OutputRoot))
        {
            if (job.ProjectId is null || job.ProjectId == Guid.Empty)
            {
                return Result<DeleteTrainingJobResult>.Failure(
                    "任务缺少项目归属，无法安全清理运行目录。OutputRoot 保留未删："
                    + job.OutputRoot);
            }

            Result<TrainingRunCleanupResult> cleanup;
            try
            {
                cleanup = await _runCleanup.DeleteRunAsync(
                    job.ProjectId.Value,
                    job.OutputRoot,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return Result<DeleteTrainingJobResult>.Failure(
                    "清理训练运行目录已取消；任务记录未删除，可重试。");
            }

            if (cleanup.IsFailure)
            {
                return Result<DeleteTrainingJobResult>.Failure(
                    cleanup.Error ?? "清理训练运行目录失败");
            }

            runDirectoryDeleted = cleanup.Value?.DirectoryDeleted ?? false;
            runDirectoryAlreadyMissing = cleanup.Value?.AlreadyMissing ?? false;
        }

        await _repository.DeleteAsync(request.JobId, cancellationToken);
        return Result<DeleteTrainingJobResult>.Success(
            new DeleteTrainingJobResult(runDirectoryDeleted, runDirectoryAlreadyMissing));
    }
}
