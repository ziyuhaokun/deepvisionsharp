using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Galleries;

public sealed record AnnotationSaveItem(
    string FileName,
    double ImageWidth,
    double ImageHeight,
    string? ImageClass = null);

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

    Task SaveBatchAsync(
        Guid galleryId,
        IReadOnlyList<AnnotationSaveItem> items,
        CancellationToken cancellationToken = default);

    Task DeleteSampleAsync(Guid galleryId, string fileName, CancellationToken cancellationToken = default);

    string GetImagePath(Guid projectId, Guid galleryId, string fileName);
}
