namespace DeepVisionSharp.Infrastructure.Persistence;

internal static class SqliteSchemaVersions
{
    /// <summary>
    /// Bump when entity/index definitions change and <c>CodeFirst.SyncStructure</c> must run.
    /// </summary>
    /// <summary>v3: Projects.CreatedUtc for accurate Project.CreatedAt rehydration.</summary>
    /// <summary>v4: Galleries.AnchorFileName for labeling resume anchor.</summary>
    /// <summary>v5: legacy Segmentation projects migrate once to SemanticSegmentation.</summary>
    /// <summary>v6: TrainingJobs.FailureMessage persists the user-visible training failure reason.</summary>
    /// <summary>v7: Galleries.SystemKind for unsupervised-anomaly system gallery identity (dropped in v10).</summary>
    /// <summary>v8: unique (GalleryId, FileName) on Samples and Annotations; dedupe legacy duplicates first.</summary>
    /// <summary>v9: TrainingJobs.LastModelPath for dual checkpoint (best / last) save.</summary>
    /// <summary>v10: drop the archived Galleries.SystemKind column.</summary>
    public const int Current = 10;
}
