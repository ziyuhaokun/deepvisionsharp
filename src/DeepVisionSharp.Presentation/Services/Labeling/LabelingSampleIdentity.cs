using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Presentation.Services.Labeling;

/// <summary>
/// [INPUT]: reads gallery and active sample from LabelingWorkspaceContext.
/// [OUTPUT]: provides a frozen sample identity and a nullable-safe IoU formatter.
/// [POS]: shared guard for LabelingInferenceRunner and LabelingInferenceDebug. An async
/// inference result is applied only if the editor still shows the captured sample.
/// [PROTOCOL]: Update this header on change, then check GEMINI.md
/// </summary>
public readonly record struct LabelingSampleIdentity(
    Guid? ProjectId,
    Guid? WorkspaceProjectId,
    Guid? GalleryId,
    Guid? SampleId,
    string FileName,
    string ImagePath)
{
    public static LabelingSampleIdentity Capture(LabelingWorkspaceContext context) =>
        new(
            context.Gallery?.ProjectId,
            context.Project?.Id,
            context.Gallery?.Id,
            context.ActiveSampleId,
            context.ActiveFileName,
            context.ActiveImagePath ?? string.Empty);

    public bool Matches(LabelingWorkspaceContext context) =>
        context.Gallery?.ProjectId == ProjectId
        && context.Project?.Id == WorkspaceProjectId
        && context.Gallery?.Id == GalleryId
        && context.ActiveSampleId == SampleId
        && string.Equals(context.ActiveFileName, FileName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(context.ActiveImagePath, ImagePath, StringComparison.OrdinalIgnoreCase);

    public static string FormatNmsIou(InferenceConfig config) =>
        config.NmsIouThreshold > InferenceConfig.UseModelNmsIou
            ? config.NmsIouThreshold.ToString("F2")
            : "model";
}
