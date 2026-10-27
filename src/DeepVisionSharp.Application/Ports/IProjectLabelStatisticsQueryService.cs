namespace DeepVisionSharp.Application.Ports;

public interface IProjectLabelStatisticsQueryService
{
    Task<ProjectLabelStatisticsSnapshot> GetStatisticsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Label sample counts for a single gallery (SampleLabelIndex rows).
    /// </summary>
    Task<ProjectLabelStatisticsSnapshot> GetGalleryStatisticsAsync(
        Guid galleryId,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectLabelStatisticsSnapshot(
    IReadOnlyList<ProjectLabelCountRow> LabelCounts,
    IReadOnlyList<ProjectGalleryLabelCountRow> GalleryLabelCounts);

public sealed record ProjectLabelCountRow(string Label, int SampleCount);

public sealed record ProjectGalleryLabelCountRow(
    Guid GalleryId,
    string GalleryName,
    string Label,
    int SampleCount);
