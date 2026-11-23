using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Presentation.Services.Labeling.WriteBack;

public interface ILabelingInferenceWriteBackStrategy
{
    InferenceWriteBackKind Kind { get; }

    Task<LabelingWriteBackResult> WriteBackAsync(LabelingWriteBackContext context, CancellationToken cancellationToken);
}

public enum LabelingWriteBackStatus
{
    Saved,
    Skipped,
    Failed
}

public sealed record LabelingWriteBackResult(LabelingWriteBackStatus Status, string Message);

public sealed class LabelingWriteBackContext
{
    public required InferenceResult Inference { get; init; }

    public required LabelingSampleIdentity Target { get; init; }

    public required LabelingWorkspaceContext Workspace { get; init; }

    public required LabelingEditorViewModel Editor { get; init; }

    public required LabelingLabelsViewModel Labels { get; init; }
}

public sealed class LabelingInferenceWriteBackRegistry
{
    private readonly IReadOnlyDictionary<InferenceWriteBackKind, ILabelingInferenceWriteBackStrategy> _strategies;

    public LabelingInferenceWriteBackRegistry(IEnumerable<ILabelingInferenceWriteBackStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(item => item.Kind);
    }

    public bool TryResolve(InferenceWriteBackKind kind, out ILabelingInferenceWriteBackStrategy strategy)
        => _strategies.TryGetValue(kind, out strategy!);
}
