using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling.WriteBack;

public interface ILabelingInferenceWriteBackStrategy
{
    InferenceWriteBackKind Kind { get; }

    Task WriteBackAsync(LabelingWriteBackContext context, CancellationToken cancellationToken = default);
}

public sealed class LabelingWriteBackContext
{
    public required InferenceResult Inference { get; init; }

    public required ResolvedTaskProfile Profile { get; init; }

    public required LabelingWorkspaceContext Workspace { get; init; }

    public required LabelingEditorViewModel Editor { get; init; }

    public required LabelingLabelsViewModel Labels { get; init; }

    public required InferenceModelOption Model { get; init; }

    public required ILogger Logger { get; init; }

    public required ShellSnackbarNotifier Snackbar { get; init; }
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
