using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Training;

namespace DeepVisionSharp.Infrastructure.Services;

public sealed class TrainingJobReconcileHostedService : BackgroundService
{
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(60);

    private readonly ITrainingJobLifecycleService _lifecycle;
    private readonly ILogger<TrainingJobReconcileHostedService> _logger;

    public TrainingJobReconcileHostedService(
        ITrainingJobLifecycleService lifecycle,
        ILogger<TrainingJobReconcileHostedService> logger)
    {
        _lifecycle = lifecycle;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ReconcileInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                var result = await _lifecycle.ReconcileOrphanedJobsAsync(stoppingToken).ConfigureAwait(false);
                if (result.IsFailure)
                {
                    _logger.LogWarning("Periodic training job reconcile failed: {Error}", result.Error);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic training job reconcile failed");
            }
        }
    }
}
