namespace DeepVisionSharp.Infrastructure.Services;

internal interface ITrainingEnvironmentChecker
{
    string CheckerKey { get; }

    bool CanHandle(TrainingEnvironmentCheckContext context);

    Task<TrainingEnvironmentCheckResult> CheckAsync(
        TrainingEnvironmentCheckContext context,
        CancellationToken cancellationToken);
}
