using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Application.Training;

/// <summary>
/// Training job session port (channel B): start/stop/reconcile + <see cref="JobStatusChanged"/>.
/// <c>StartTrainingJobCommand</c> is the CQRS façade (channel A) that delegates to <see cref="StartAsync"/>.
/// </summary>
public interface ITrainingJobLifecycleService
{
    Task<Result<StartTrainingJobResult>> StartAsync(
        StartTrainingJobCommand request,
        CancellationToken cancellationToken = default);

    Task<Result> StopAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<Result<Guid>> DuplicateAsDraftAsync(
        Guid sourceJobId,
        Guid? targetProjectId = null,
        CancellationToken cancellationToken = default);

    Task<Result<int>> ReconcileOrphanedJobsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True while a training run is queued or executing in this process. The shell uses it to warn
    /// before closing the window, because closing cancels the run.
    /// </summary>
    bool HasActiveRun { get; }

    event Action<Guid, TrainingJobStatus, string?>? JobStatusChanged;
}
