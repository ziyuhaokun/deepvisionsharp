using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Project workflow kind: image classification and object detection.
/// </summary>
public enum ProjectWorkflowKind
{
    Classification,
    Detection
}

/// <summary>
/// Resolves the workflow of a supported project type and exposes its
/// stable capability queries. Archived workflows return false everywhere.
/// </summary>
public static class ProjectWorkflow
{
    public static ProjectWorkflowKind Resolve(string? projectType)
    {
        var normalized = ProjectTypes.Normalize(projectType);
        return ProjectTypes.IsDetection(normalized)
            ? ProjectWorkflowKind.Detection
            : ProjectWorkflowKind.Classification;
    }

    /// <summary>No project selected keeps the classification workspace shell.</summary>
    public static ProjectWorkflowKind Resolve(Project? project) =>
        project is null ? ProjectWorkflowKind.Classification : Resolve(project.Type);

    public static bool IsClassification(ProjectWorkflowKind kind) =>
        kind == ProjectWorkflowKind.Classification;

    public static bool IsDetection(ProjectWorkflowKind kind) =>
        kind == ProjectWorkflowKind.Detection;
}
