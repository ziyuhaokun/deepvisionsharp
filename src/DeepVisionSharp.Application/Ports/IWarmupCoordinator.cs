using DeepVisionSharp.Application.Inference;

namespace DeepVisionSharp.Application.Ports;

/// <summary>What to warm up: the same arguments <see cref="IInferenceRunner.WarmupAsync"/> takes.</summary>
public sealed record WarmupRequest(string ModelPath, string ModelType, InferenceConfig Config)
{
    /// <summary>Identity used to collapse repeated requests for the same model and device.</summary>
    public string Key => $"{ModelPath}|{ModelType}|{Config.Device}";
}

/// <summary>The result of one warm-up attempt.</summary>
/// <param name="Cancelled">
/// The attempt was stopped on purpose (a newer target arrived, or <c>Cancel</c> was called). Native
/// loads cannot be interrupted, so this is only known once the call has returned.
/// </param>
public sealed record WarmupOutcome(
    WarmupRequest Request,
    bool Success,
    bool Cancelled,
    string? Error,
    TimeSpan Elapsed);

/// <summary>
/// Runs at most one model warm-up at a time and keeps only the latest target.
/// </summary>
/// <remarks>
/// Warm-up loads native weights, so two of them at once means two resident models on one device.
/// Requests are therefore serialised: a new target waits for the running load to finish (it is also
/// asked to cancel, but a native load that ignores the token just runs to completion). Intermediate
/// targets are dropped — only the first and the latest matter. <see cref="WaitForIdleAsync"/> exists
/// so a page teardown can wait for the load instead of leaving it running unsupervised.
/// </remarks>
public interface IWarmupCoordinator
{
    /// <summary>True while a warm-up call is actually in progress.</summary>
    bool IsRunning { get; }

    /// <summary>Key of the most recently requested target; null after <see cref="Cancel"/>.</summary>
    string? DesiredKey { get; }

    /// <summary>Asks for a warm-up. Repeating the running or the queued target is a no-op.</summary>
    void Request(WarmupRequest request);

    /// <summary>Drops the queued target and asks the running one to stop.</summary>
    void Cancel();

    /// <summary>
    /// Waits until nothing is running or queued. Returns false when <paramref name="timeout"/> elapsed
    /// first (native work still running); it never throws for that.
    /// </summary>
    Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Raised on a pool thread after every attempt, including cancelled ones.</summary>
    event Action<WarmupOutcome>? Completed;
}
