using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

/// <summary>
/// Shell 侧工作流解析：统一委托 Domain <see cref="ProjectWorkflow"/>，
/// 并在工作区项目与当前选中项目之间做兜底解析。
/// </summary>
internal static class LabelingProjectWorkflow
{
    public static ProjectWorkflowKind ResolveKind(Project? workspaceProject, Project? selectedProject)
    {
        if (workspaceProject is not null)
        {
            return ProjectWorkflow.Resolve(workspaceProject);
        }

        return ProjectWorkflow.Resolve(selectedProject);
    }
}
