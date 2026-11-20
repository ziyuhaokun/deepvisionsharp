using DeepVisionSharp.Presentation.Services;

namespace DeepVisionSharp.Presentation.Ports;

/// <summary>
/// CPU / memory / GPU load sampling port. Performance counters and <c>nvidia-smi</c> are host
/// platform details, so the sampler itself lives with the shell.
/// </summary>
public interface ISystemResourceMonitor
{
    Task<TrainingResourceUsage> GetUsageAsync(CancellationToken cancellationToken = default);
}
