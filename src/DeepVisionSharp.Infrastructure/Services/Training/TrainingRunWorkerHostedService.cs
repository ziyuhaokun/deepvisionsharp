using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Executes prepared training runs outside the start command.
/// </summary>
/// <remarks>
/// <c>StartTrainingJobCommand</c> returns once the job is <see cref="TrainingJobStatus.Preparing"/>;
/// dataset export, training, checkpoint save and the terminal status all happen here. Terminal
/// states are written through <see cref="TrainingJobStatusWriter"/>, whose event already feeds the
/// shell's status pipeline, so no new notification channel is needed.
/// </remarks>
internal sealed class TrainingRunWorkerHostedService : BackgroundService
{
    private readonly TrainingRunQueue _queue;
    private readonly TrainingJobStatusWriter _statusWriter;
    private readonly ITrainingJobRepository _repository;
    private readonly ILogger<TrainingRunWorkerHostedService> _logger;

    public TrainingRunWorkerHostedService(
        TrainingRunQueue queue,
        TrainingJobStatusWriter statusWriter,
        ITrainingJobRepository repository,
        ILogger<TrainingRunWorkerHostedService> logger)
    {
        _queue = queue;
        _statusWriter = statusWriter;
        _repository = repository;
        _logger = logger;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var pending = _queue.Shutdown();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        foreach (var request in pending)
        {
            await EnsureCancelledAsync(request.JobId).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var work in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
        {
            // Link the host lifetime: a graceful shutdown cancels the run (and it lands Cancelled)
            // instead of leaving the job Preparing for the next startup to mark Failed.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(work.Token, stoppingToken);
            try
            {
                await RunAsync(work.Request, linked.Token).ConfigureAwait(false);
            }
            finally
            {
                work.Request.GateLease.Dispose();
                _queue.Complete(work.Request.JobId);
            }
        }
    }

    private async Task RunAsync(TrainingRunRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await request.Backend
                .StartAsync(request.Context, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsFailure)
            {
                // The backend usually marks the terminal state itself; this only covers a failure
                // that left the job Preparing/Running (for example a cancelled queue item).
                if (cancellationToken.IsCancellationRequested)
                {
                    await EnsureCancelledAsync(request.JobId).ConfigureAwait(false);
                }
                else
                {
                    await EnsureFailedAsync(request.JobId, result.Error ?? "训练失败").ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await EnsureCancelledAsync(request.JobId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background training run failed for job {JobId}", request.JobId);
            await EnsureFailedAsync(request.JobId, ex.Message).ConfigureAwait(false);
        }
    }

    private async Task EnsureFailedAsync(Guid jobId, string failureMessage)
    {
        var job = await _repository.GetByIdAsync(jobId, CancellationToken.None).ConfigureAwait(false);
        if (job is null || !TrainingJobTransitions.IsActive(job.Status))
        {
            return;
        }

        _logger.LogWarning("Training job {JobId} left active by its backend: {Reason}", jobId, failureMessage);
        await _statusWriter.MarkFailedAsync(job, CancellationToken.None, failureMessage).ConfigureAwait(false);
    }

    private async Task EnsureCancelledAsync(Guid jobId)
    {
        var job = await _repository.GetByIdAsync(jobId, CancellationToken.None).ConfigureAwait(false);
        if (job is null || !TrainingJobTransitions.IsActive(job.Status))
        {
            return;
        }

        await _statusWriter.MarkCancelledAsync(job, CancellationToken.None).ConfigureAwait(false);
    }
}
