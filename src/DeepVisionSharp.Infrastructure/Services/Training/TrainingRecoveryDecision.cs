namespace DeepVisionSharp.Infrastructure.Services;

internal enum TrainingRecoveryAction
{
    KeepRunning,
    MarkSucceeded,
    MarkFailed,
    MarkCancelled
}

internal sealed record TrainingRecoveryDecision(
    TrainingRecoveryAction Action,
    string? BestModelPath = null,
    string? LastModelPath = null,
    string? Reason = null);
