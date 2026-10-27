using DeepVisionSharp.Application.Inference;

namespace DeepVisionSharp.Application.Ports;

public interface IInferenceRunner
{
    Task<InferenceResult> RunSingleAsync(
        string modelPath,
        string imagePath,
        InferenceConfig config,
        string modelType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Best-effort model preflight + load so the next <see cref="RunSingleAsync"/> only pays infer cost.
    /// </summary>
    Task<InferenceWarmupResult> WarmupAsync(
        string modelPath,
        InferenceConfig config,
        string modelType,
        CancellationToken cancellationToken);

    Task StopAsync(string? reason, CancellationToken cancellationToken);
}

public sealed class InferenceWarmupResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public double PreflightMilliseconds { get; init; }

    public double LoadMilliseconds { get; init; }

    public bool PreflightFromCache { get; init; }

    public bool LoadSkipped { get; init; }

    public static InferenceWarmupResult Ok(
        double preflightMs,
        double loadMs,
        bool preflightCached,
        bool loadSkipped) =>
        new()
        {
            Success = true,
            PreflightMilliseconds = preflightMs,
            LoadMilliseconds = loadMs,
            PreflightFromCache = preflightCached,
            LoadSkipped = loadSkipped
        };

    public static InferenceWarmupResult Fail(string error) =>
        new()
        {
            Success = false,
            Error = error
        };
}

public interface IInferenceStatusReader
{
    bool IsInferenceRunning { get; }
}
