using FreeSql.DataAnnotations;

namespace DeepVisionSharp.Infrastructure.Persistence;

[Table(Name = "Projects")]
internal sealed class SqlProject
{
    [Column(IsPrimary = true)]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public int SampleCount { get; set; }

    public double Progress { get; set; }

    /// <summary>UTC creation time. Null/default on legacy rows until first save after schema v3.</summary>
    public DateTime? CreatedUtc { get; set; }

    public DateTime LastUpdatedUtc { get; set; }
}

[Table(Name = "ProjectLabels")]
[Index("IX_ProjectLabels_ProjectId", "ProjectId")]
internal sealed class SqlProjectLabel
{
    [Column(IsPrimary = true)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;
}

[Table(Name = "Galleries")]
[Index("IX_Galleries_ProjectId", "ProjectId")]
internal sealed class SqlGallery
{
    [Column(IsPrimary = true)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int SampleCount { get; set; }

    public int AnnotatedCount { get; set; }

    public DateTime LastUpdatedUtc { get; set; }

    /// <summary>Optional labeling resume anchor: sample file name within this gallery.</summary>
    public string? AnchorFileName { get; set; }
}

[Table(Name = "Samples")]
[Index("IX_Samples_GalleryId", "GalleryId")]
// v8+: unique per gallery file name (dedupe runs before schema upgrade creates the index).
[Index("UK_Samples_GalleryId_FileName", "GalleryId,FileName", true)]
internal sealed class SqlSample
{
    [Column(IsPrimary = true)]
    public Guid Id { get; set; }

    public Guid GalleryId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public bool IsAnnotated { get; set; }

    public bool HasError { get; set; }

    public string? ErrorMessage { get; set; }
}

[Table(Name = "Models")]
[Index("IX_Models_ProjectId", "ProjectId")]
internal sealed class SqlModel
{
    [Column(IsPrimary = true)]
    public Guid Id { get; set; }

    public Guid ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string ModelType { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public string ClassesJson { get; set; } = "[]";

    public string CheckpointPath { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public int MaxDetections { get; set; }
}

[Table(Name = "TrainingJobs")]
[Index("IX_TrainingJobs_Status", "Status")]
[Index("IX_TrainingJobs_ProjectId", "ProjectId")]
internal sealed class SqlTrainingJob
{
    [Column(IsPrimary = true)]
    public Guid Id { get; set; }

    public Guid? ProjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Backend { get; set; } = string.Empty;

    public string ModelType { get; set; } = string.Empty;

    public string? DataRoot { get; set; }

    public string? OutputRoot { get; set; }

    public string Status { get; set; } = string.Empty;

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public string? BestModelPath { get; set; }

    public string? LastModelPath { get; set; }

    public string? LogFile { get; set; }

    public int? ProcessId { get; set; }

    public string? FailureMessage { get; set; }

    public string ConfigurationJson { get; set; } = "{}";
}

[Table(Name = "Annotations")]
[Index("IX_Annotations_GalleryId", "GalleryId")]
// v8+: unique per gallery file name (dedupe runs before schema upgrade creates the index).
[Index("UK_Annotations_GalleryId_FileName", "GalleryId,FileName", true)]
internal sealed class SqlAnnotation
{
    [Column(IsPrimary = true, IsIdentity = true)]
    public long Id { get; set; }

    public Guid GalleryId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public double Width { get; set; }

    public double Height { get; set; }

    public string? ImageData { get; set; }

    public string DocumentJson { get; set; } = "{}";
}

[Table(Name = "SampleLabelIndex")]
[Index("IX_SampleLabelIndex_GalleryId_Label", "GalleryId,Label")]
[Index("IX_SampleLabelIndex_GalleryId_FileName", "GalleryId,FileName")]
internal sealed class SqlSampleLabelIndex
{
    [Column(IsPrimary = true)]
    public Guid GalleryId { get; set; }

    [Column(IsPrimary = true)]
    public string FileName { get; set; } = string.Empty;

    [Column(IsPrimary = true)]
    public string Label { get; set; } = string.Empty;
}
