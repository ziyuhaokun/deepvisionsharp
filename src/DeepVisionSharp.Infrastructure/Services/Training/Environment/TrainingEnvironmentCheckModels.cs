using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Infrastructure.Services;

internal enum TrainingEnvironmentIssueSeverity
{
    Error,
    Warning,
    Info
}

internal sealed record TrainingEnvironmentIssue(
    string Code,
    string Message,
    string? Remediation = null,
    TrainingEnvironmentIssueSeverity Severity = TrainingEnvironmentIssueSeverity.Error);

internal sealed record TrainingEnvironmentCheckContext(
    StartTrainingJobCommand Command,
    ITrainingBackend Backend,
    IReadOnlyList<Guid> ParticipatingGalleryIds,
    IReadOnlyList<ProjectGallery> Galleries,
    IReadOnlyDictionary<Guid, int> AnnotatedCounts);

internal sealed record TrainingEnvironmentCheckResult(
    IReadOnlyList<TrainingEnvironmentIssue> Errors,
    IReadOnlyList<TrainingEnvironmentIssue> Warnings,
    IReadOnlyList<TrainingEnvironmentIssue> Notes)
{
    public bool Success => Errors.Count == 0;

    public string SummaryMessage
    {
        get
        {
            var firstError = Errors.FirstOrDefault();
            if (firstError is null)
            {
                return "训练环境检查通过";
            }

            return string.IsNullOrWhiteSpace(firstError.Remediation)
                ? $"训练环境未就绪：{firstError.Message}"
                : $"训练环境未就绪：{firstError.Message}。{firstError.Remediation}";
        }
    }

    public static TrainingEnvironmentCheckResult Passed { get; } = new([], [], []);
}
