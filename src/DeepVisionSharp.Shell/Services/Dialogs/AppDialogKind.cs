namespace DeepVisionSharp.Shell.Services.Dialogs;

public enum AppDialogKind
{
    AddLabel,
    EditLabelColor,
    /// <summary>General image classification dialog.</summary>
    AutoInferenceClassification,
    InferenceDebug,
    ProjectLabelStatistics,
    GalleryBatchInference,
    /// <summary>Inference page: pick external files or gallery samples.</summary>
    InferenceImageSource,
    /// <summary>Inference page: batch run analysis report (miss rate, timings).</summary>
    InferenceBatchReport,
    CreateProject,
    EditProject,
    CreateGallery,
    EditGallery,
    EditModel,
    Confirm,
    RenameTrainingJob,
    GalleryMerge,
    GalleryMoveSamples,
    GallerySamplePicker,
    GallerySplitPlanner,
    SettingsDebugAccess,
}
