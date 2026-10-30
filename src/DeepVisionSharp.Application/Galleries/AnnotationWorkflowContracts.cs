using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Galleries;

public sealed record AnnotationSaveItem(
    string FileName,
    double ImageWidth,
    double ImageHeight,
    string? ImageClass = null,
    IReadOnlyList<AnnotationShape>? Shapes = null,
    bool IsReviewed = false);

/// <summary>
/// Labeling workspace port (channel B): high-frequency load/save/path per sample.
/// Optional CQRS wrappers (<c>SaveAnnotationsCommand</c>, etc.) thin-delegate here.
/// Shell may inject this directly; bypasses MediatR pipeline by design.
/// </summary>
public interface IAnnotationWorkflowService
{
    Task<AnnotationDocument> LoadAsync(
        Guid galleryId,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<string, string>> LoadImageClassesBatchAsync(
        Guid galleryId,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        Guid galleryId,
        string fileName,
        double imageWidth,
        double imageHeight,
        string? imageClass = null,
        CancellationToken cancellationToken = default);

    /// <summary>Saves a class and/or box shapes. Detection samples also set <paramref name="isReviewed"/>.</summary>
    Task SaveAsync(
        Guid galleryId,
        string fileName,
        double imageWidth,
        double imageHeight,
        string? imageClass,
        IReadOnlyList<AnnotationShape>? shapes,
        bool isReviewed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads full documents for a batch of samples in one query. Used by dataset export so a
    /// detection export does not re-read the gallery summary per image.
    /// </summary>
    Task<IReadOnlyDictionary<string, AnnotationDocument>> LoadDocumentsBatchAsync(
        Guid galleryId,
        IReadOnlyList<string> fileNames,
        CancellationToken cancellationToken = default);

    Task SaveBatchAsync(
        Guid galleryId,
        IReadOnlyList<AnnotationSaveItem> items,
        CancellationToken cancellationToken = default);

    Task DeleteSampleAsync(Guid galleryId, string fileName, CancellationToken cancellationToken = default);

    string GetImagePath(Guid projectId, Guid galleryId, string fileName);
}
