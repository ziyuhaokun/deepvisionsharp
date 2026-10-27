using DeepVisionSharp.Domain.Entities;

namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Project workflow kind. 1.0 exposes image classification only.
/// </summary>
public enum ProjectWorkflowKind
{
    Classification
}

/// <summary>
/// Resolves the workflow of the single supported project type and exposes its
/// stable capability queries. Archived workflows return false everywhere.
/// </summary>
public static class ProjectWorkflow
{
    public static ProjectWorkflowKind Resolve(string? projectType)
    {
        ProjectTypes.Normalize(projectType);
        return ProjectWorkflowKind.Classification;
    }

    /// <summary>No project selected keeps the classification workspace shell.</summary>
    public static ProjectWorkflowKind Resolve(Project? project) =>
        project is null ? ProjectWorkflowKind.Classification : Resolve(project.Type);

    public static bool IsClassification(ProjectWorkflowKind kind) =>
        kind == ProjectWorkflowKind.Classification;
}
