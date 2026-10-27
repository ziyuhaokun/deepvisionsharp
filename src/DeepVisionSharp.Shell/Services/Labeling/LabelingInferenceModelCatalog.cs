using System.IO;

using MediatR;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// Loads and filters inference model options for the labeling auto-inference panel.
/// Owned by <see cref="LabelingInferenceViewModel"/>.
/// </summary>
public sealed class LabelingInferenceModelCatalog
{
    private readonly ILabelingInferenceHost _host;
    private readonly IMediator _mediator;
    private readonly ITaskCapabilityResolver _taskCapabilities;

    public LabelingInferenceModelCatalog(
        ILabelingInferenceHost host,
        IMediator mediator,
        ITaskCapabilityResolver taskCapabilities)
    {
        _host = host;
        _mediator = mediator;
        _taskCapabilities = taskCapabilities;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var previousSelection = _host.SelectedInferenceModel;

        _host.InferenceModels.Clear();
        _host.NotifyModelsListChanged();
        _host.NotifyInferenceConfigState();

        var context = _host.Context;
        if (context.Gallery is null)
        {
            _host.NotifyInferenceCommandsCanExecute();
            return;
        }

        var modelsResult = await _mediator.Send(
            new GetModelsByProjectQuery(context.Gallery.ProjectId),
            cancellationToken);
        if (modelsResult.IsFailure || modelsResult.Value is null)
        {
            return;
        }

        var projectType = context.Project?.Type;
        if (string.IsNullOrWhiteSpace(projectType))
        {
            _host.NotifyInferenceCommandsCanExecute();
            return;
        }

        var loadedModels = new List<InferenceModelOption>();
        foreach (var model in modelsResult.Value.Where(item =>
                     !string.IsNullOrWhiteSpace(item.CheckpointPath)
                     && File.Exists(item.CheckpointPath)
                     && IsInferenceEligibleStatus(item.Status)
                     && !string.IsNullOrWhiteSpace(item.ModelType)))
        {
            var profileResult = _taskCapabilities.ForProjectAndModel(projectType, model.ModelType);
            if (profileResult.IsFailure
                || profileResult.Value is null
                || profileResult.Value.WriteBackKind is InferenceWriteBackKind.None)
            {
                // Auto-inference only lists models with a Shell write-back strategy.
                continue;
            }

            if (profileResult.Value.WriteBackKind is InferenceWriteBackKind.ImageClassTop1)
            {
                // Class identity is set-based (order-insensitive). Training freezes project.Labels order
                // (SQLite insertion order), while LabelOptions is sorted by name — SequenceEqual used to
                // hide valid classification models from the inference dialog.
                var projectClasses = _host.Labels.LabelOptions.Select(item => item.Name);
                if (!ClassSetsMatch(model.Classes, projectClasses))
                {
                    continue;
                }
            }

            var metadataResult = await _mediator.Send(
                new GetModelDisplayMetricsQuery(model.CheckpointPath),
                cancellationToken);
            var inputSize = metadataResult.IsSuccess
                ? metadataResult.Value?.InputSize
                : null;
            loadedModels.Add(new InferenceModelOption(
                model.Id,
                model.Name,
                model.ModelType,
                model.CheckpointPath,
                inputSize));
        }

        // Prefer classification models first.
        loadedModels = loadedModels
            .OrderByDescending(item => ModelTypeNames.IsClassificationModelType(item.ModelType))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();
        _host.InferenceModels.Clear();
        foreach (var model in loadedModels)
        {
            _host.InferenceModels.Add(model);
        }

        _host.SelectedInferenceModel =
            TryRestoreSelection(previousSelection, _host.InferenceModels)
            ?? _host.InferenceModels.FirstOrDefault();
        _host.NotifyModelsListChanged();
        _host.NotifyInferenceConfigState();
        _host.NotifyInferenceCommandsCanExecute();
    }

    /// <summary>重新加载模型列表后，按模型 Id 与 checkpoint 恢复上次选择。</summary>
    internal static InferenceModelOption? TryRestoreSelection(
        InferenceModelOption? previous,
        IEnumerable<InferenceModelOption> models)
    {
        if (previous is null)
        {
            return null;
        }

        return models.FirstOrDefault(item =>
            item.Id == previous.Id
            && string.Equals(item.CheckpointPath, previous.CheckpointPath, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSpatialPolicyCompatible(ResolvedTaskProfile profile, InferenceModelOption model) =>
        model.InputSize is > 0;

    public static string GetSpatialPolicyErrorMessage(ResolvedTaskProfile profile) =>
        "所选分类模型元数据无效，不能用于分类推理。";

    /// <summary>
    /// Ready and Published models may run auto-inference; draft/pending/archived are excluded.
    /// Delegates to the domain rule so every inference surface agrees.
    /// </summary>
    internal static bool IsInferenceEligibleStatus(TrainedModelStatus status) =>
        TrainedModelStatuses.CanRunInference(status);

    /// <summary>
    /// True when both sides list the same class names (case-insensitive, order-insensitive, trimmed).
    /// Empty sides never match — prevents listing models before project labels are loaded.
    /// </summary>
    internal static bool ClassSetsMatch(
        IReadOnlyList<string>? modelClasses,
        IEnumerable<string>? projectClasses)
    {
        var modelSet = NormalizeClassSet(modelClasses);
        var projectSet = NormalizeClassSet(projectClasses);
        return modelSet.Count > 0
               && projectSet.Count > 0
               && modelSet.SetEquals(projectSet);
    }

    private static HashSet<string> NormalizeClassSet(IEnumerable<string>? classes)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (classes is null)
        {
            return set;
        }

        foreach (var item in classes)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            set.Add(item.Trim());
        }

        return set;
    }
}
