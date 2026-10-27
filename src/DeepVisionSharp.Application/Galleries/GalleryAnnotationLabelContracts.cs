namespace DeepVisionSharp.Application.Galleries;

public enum GalleryAnnotationLabelMatch
{
    Any,
    Only
}

public interface IGalleryAnnotationLabelQueryService
{
    Task<IReadOnlyList<string>> ListDistinctLabelsAsync(
        Guid galleryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSampleFileNamesWithLabelAsync(
        Guid galleryId,
        string label,
        GalleryAnnotationLabelMatch match = GalleryAnnotationLabelMatch.Any,
        CancellationToken cancellationToken = default);
}
