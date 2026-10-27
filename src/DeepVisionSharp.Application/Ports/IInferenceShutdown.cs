namespace DeepVisionSharp.Application.Ports;

public interface IInferenceShutdown
{
    Task StopAndDisposeAsync(string? reason = null, CancellationToken cancellationToken = default);
}
