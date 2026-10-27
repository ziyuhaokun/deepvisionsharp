using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Training;

namespace DeepVisionSharp.Infrastructure.Services;

public sealed class TrainingJobRecoveryHostedService : IHostedService
{
    private readonly ITrainingJobLifecycleService _lifecycle;
    private readonly ILogger<TrainingJobRecoveryHostedService> _logger;

    public TrainingJobRecoveryHostedService(
        ITrainingJobLifecycleService lifecycle,
        ILogger<TrainingJobRecoveryHostedService> logger)
    {
        _lifecycle = lifecycle;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var result = await _lifecycle.ReconcileOrphanedJobsAsync(cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            _logger.LogWarning("Training job recovery failed: {Error}", result.Error);
            return;
        }

        if (result.Value > 0)
        {
            _logger.LogInformation("Recovered {Count} orphaned training job(s) on startup", result.Value);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
