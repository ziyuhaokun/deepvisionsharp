using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.Services.Labeling.InferenceDialogs;

/// <summary>
/// Resolves the auto-inference dialog session for a labeling project workflow.
/// Only classification is an open workflow; archived kinds return null.
/// </summary>
public static class InferenceDialogSessionFactory
{
    public static IInferenceDialogSession? Resolve(ProjectWorkflowKind workflow) =>
        workflow switch
        {
            ProjectWorkflowKind.Classification => ClassificationInferenceDialogSession.Instance,
            _ => null
        };
}
