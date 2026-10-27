namespace DeepVisionSharp.Shell.Services.Tasks;

public enum BackgroundTaskKind
{
    Training,
    Export,
    StorageCleanup,
    /// <summary>Labeling-page auto-inference for the classification workflow.</summary>
    Inference,
    /// <summary>Gallery-card batch inference over all/filtered samples.</summary>
    GalleryBatchInference
}

public enum BackgroundTaskStatus
{
    Queued,
    Running,
    Succeeded,
    Failed,
    Cancelled
}
