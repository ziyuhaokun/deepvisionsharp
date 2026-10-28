namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>Lifecycle of <see cref="IInferenceRuntime"/>.</summary>
internal enum InferenceRuntimeState
{
    /// <summary>Accepting new operations.</summary>
    Running,

    /// <summary>
    /// An exclusive holder (training) is draining inference or owns the device. New operations are
    /// rejected until its lease is disposed.
    /// </summary>
    Exclusive,

    /// <summary>Shutdown began; new operations are rejected and active ones are being drained.</summary>
    ShuttingDown,

    /// <summary>Shutdown completed; nothing is running.</summary>
    Stopped
}

/// <summary>Raised when an operation is submitted while the runtime is exclusive or shut down.</summary>
internal sealed class InferenceRuntimeUnavailableException(string message) : InvalidOperationException(message);

/// <summary>Well-known <c>kind</c> values for registered operations.</summary>
internal static class InferenceOperationKinds
{
    public const string Inference = "inference";
    public const string Warmup = "warmup";
    public const string ClearCache = "clear-cache";
}

/// <summary>A registered operation as seen from outside the runtime (diagnostics and UI status).</summary>
/// <param name="Kind">What the operation does, see <see cref="InferenceOperationKinds"/>.</param>
/// <param name="OwnerKey">Who started it; <see langword="null"/> when the caller did not identify itself.</param>
/// <param name="IsCancelling">
/// The owner asked it to stop. Native code cannot be interrupted, so it may keep running for a while;
/// this flag is how callers tell "stopping" from "stopped".
/// </param>
internal sealed record InferenceOperationInfo(string Kind, string? OwnerKey, bool IsCancelling);

/// <summary>
/// Single owner of in-flight inference operations: registers them, stops them per owner and drains
/// them on shutdown or when training takes the device.
/// </summary>
/// <remarks>
/// Native forward/load calls cannot be interrupted, so cancellation is cooperative: the token is
/// signalled, and an operation that does not observe it simply finishes. Stop is scoped to an owner so
/// that one page cancelling its work never kills another page's; the runtime as a whole keeps
/// accepting work while an owner is stopping.
/// </remarks>
internal interface IInferenceRuntime
{
    InferenceRuntimeState State { get; }

    int ActiveOperationCount { get; }

    /// <summary>Kinds of the operations currently registered (diagnostics).</summary>
    IReadOnlyList<string> ActiveOperations { get; }

    /// <summary>Kind, owner and cancelling flag of every registered operation.</summary>
    IReadOnlyList<InferenceOperationInfo> OperationSnapshots { get; }

    /// <summary>Raised after every state transition, outside the runtime's internal lock.</summary>
    event Action<InferenceRuntimeState>? StateChanged;

    /// <summary>
    /// Runs <paramref name="work"/> as a registered operation on the thread pool.
    /// </summary>
    /// <param name="ownerKey">Identifies the caller for <see cref="StopAsync"/>; may be null.</param>
    /// <exception cref="InferenceRuntimeUnavailableException">The runtime is exclusive or shut down.</exception>
    Task<T> RunAsync<T>(
        string kind,
        Func<CancellationToken, T> work,
        CancellationToken cancellationToken,
        string? ownerKey = null);

    /// <summary>
    /// Like <see cref="RunAsync{T}"/> for work that is already asynchronous (for example a backend
    /// call). The operation stays registered until the returned task of <paramref name="work"/> ends.
    /// </summary>
    /// <exception cref="InferenceRuntimeUnavailableException">The runtime is exclusive or shut down.</exception>
    Task<T> ExecuteAsync<T>(
        string kind,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken,
        string? ownerKey = null);

    /// <summary>
    /// Takes exclusive ownership of the device for training. In order: rejects new operations,
    /// waits for registered ones to drain, takes the engine's device gate (which also waits for
    /// inference started through legacy engine entry points), then clears the model cache so the
    /// trainer starts from an empty device. Active inference is waited for, not cancelled.
    /// </summary>
    /// <returns>A lease; disposing it releases the device and re-opens the runtime.</returns>
    /// <exception cref="TimeoutException">Inference did not drain within <paramref name="drainTimeout"/>.</exception>
    /// <exception cref="InvalidOperationException">Another exclusive holder already exists.</exception>
    /// <exception cref="InferenceRuntimeUnavailableException">The runtime is shut down.</exception>
    Task<IDisposable> BeginExclusiveAsync(TimeSpan drainTimeout, CancellationToken cancellationToken);

    /// <summary>Releases cached native models as a registered operation.</summary>
    /// <exception cref="InferenceRuntimeUnavailableException">The runtime is not accepting operations.</exception>
    Task ClearCacheAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Cancels the operations registered under <paramref name="ownerKey"/> and waits for exactly those
    /// to finish; other owners' operations are neither cancelled nor awaited. A blank
    /// <paramref name="ownerKey"/> does nothing. The wait honours <paramref name="cancellationToken"/>,
    /// but the operations stay flagged as cancelling until they actually end.
    /// </summary>
    Task StopAsync(string? ownerKey, string? reason, CancellationToken cancellationToken);

    /// <summary>
    /// Rejects new operations, cancels active ones and waits up to <paramref name="timeout"/> for them
    /// to drain. Idempotent; a timed-out call leaves the runtime ShuttingDown and may be retried.
    /// </summary>
    Task ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
