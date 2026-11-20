namespace DeepVisionSharp.Domain.ValueObjects;

public sealed record ProjectCapabilityProfile
{
    internal ProjectCapabilityProfile(ProjectWorkflowKind workflow)
    {
        Workflow = workflow;
    }

    public ProjectWorkflowKind Workflow { get; }

    /// <summary>
    /// 标注画布是否用透明填充（分类工作流按整图打类，框只做提示）。
    /// deepvision 按 <c>AnnotationMode</c> 判定，本仓库只有分类 / 检测两种工作流。
    /// </summary>
    public bool SupportsImageClass => Workflow == ProjectWorkflowKind.Classification;

}

public static class ProjectCapabilityCatalog
{
    private static readonly ProjectCapabilityProfile Classification = new(ProjectWorkflowKind.Classification);

    private static readonly ProjectCapabilityProfile Detection = new(ProjectWorkflowKind.Detection);

    public static IReadOnlyList<string> ActiveProjectTypes { get; } =
        Array.AsReadOnly([ProjectTypes.Classification, ProjectTypes.Detection]);

    public static string DefaultCreationType => ProjectTypes.Classification;

    public static bool IsActive(string? projectType) => ProjectTypes.IsValid(projectType);

    public static void EnsureActive(string? projectType) => ProjectTypes.Normalize(projectType);

    public static ProjectCapabilityProfile Resolve(ProjectWorkflowKind workflow) =>
        workflow switch
        {
            ProjectWorkflowKind.Classification => Classification,
            ProjectWorkflowKind.Detection => Detection,
            _ => throw new ArgumentOutOfRangeException(nameof(workflow), workflow, "未知的项目工作流。")
        };
}
