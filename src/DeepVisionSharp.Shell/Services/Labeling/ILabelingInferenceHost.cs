using System.Collections.ObjectModel;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Shell.Services.Labeling.WriteBack;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// Host surface for labeling inference collaborators (catalog / runner / debug).
/// Implemented by <see cref="LabelingInferenceViewModel"/>.
/// </summary>
public interface ILabelingInferenceHost
{
    LabelingWorkspaceContext Context { get; }

    LabelingEditorViewModel Editor { get; }

    LabelingLabelsViewModel Labels { get; }

    /// <summary>DataContext for inference-related dialogs (the inference ViewModel).</summary>
    object DialogDataContext { get; }

    ObservableCollection<InferenceModelOption> InferenceModels { get; }

    InferenceModelOption? SelectedInferenceModel { get; set; }

    double InferenceConfidence { get; }

    int InferenceMaxDetections { get; }

    int InferenceBatchSize { get; }

    string InferenceDebugHeadline { get; set; }

    string InferenceDebugReport { get; set; }

    string InferenceDialogErrorMessage { get; }

    string InferenceNoModelsHint { get; }

    bool IsInferenceConfigValid { get; }

    bool IsClassificationModelSelected { get; }

    bool IsClassificationProject { get; }

    LabelingInferenceWriteBackRegistry WriteBackRegistry { get; }

    InferenceConfig BuildInferenceConfig();

    Result<ResolvedTaskProfile> TryResolveSelectedProfile();

    bool TryValidateInferenceDraft();

    void NotifyModelsListChanged();

    void NotifyInferenceConfigState();

    void NotifyInferenceCommandsCanExecute();
}
