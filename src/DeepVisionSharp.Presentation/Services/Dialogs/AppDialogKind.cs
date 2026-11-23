namespace DeepVisionSharp.Presentation.Services.Dialogs;

public enum AppDialogKind
{
    AddLabel,
    EditLabelColor,
    /// <summary>General image classification dialog.</summary>
    AutoInferenceClassification,
    /// <summary>Object detection auto-inference dialog.</summary>
    AutoInferenceDetection,
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
