using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Infrastructure.Services;

internal interface ITrainingBackend
{
    string BackendKey { get; }

    bool CanHandle(string? modelType);

    Task<Result> ValidateAsync(
        TrainingStartValidationContext context,
        CancellationToken cancellationToken);

    Task<Result<TrainingBackendStartResult>> StartAsync(
        TrainingStartContext context,
        CancellationToken cancellationToken);

    Task<Result> StopAsync(
        TrainingJob job,
        string? reason,
        CancellationToken cancellationToken);

    Task<TrainingRecoveryDecision> EvaluateRecoveryAsync(
        TrainingJob job,
        CancellationToken cancellationToken);
}
