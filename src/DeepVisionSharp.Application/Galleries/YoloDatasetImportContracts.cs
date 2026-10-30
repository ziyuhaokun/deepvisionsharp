namespace DeepVisionSharp.Application.Galleries;

public sealed record YoloDatasetImportRequest(Guid GalleryId, string DatasetRoot);

public sealed record YoloDatasetImportResult(
    bool Succeeded,
    int Total,
    int Imported,
    int Failed,
    int Skipped,
    int BoxCount,
    IReadOnlyList<string> Classes,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>Imports an existing YOLO dataset (images/ + labels/ + classes file) as sample annotations.</summary>
public interface IYoloDatasetImportService
{
    Task<YoloDatasetImportResult> ImportAsync(
        YoloDatasetImportRequest request,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
