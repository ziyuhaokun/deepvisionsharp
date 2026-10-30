using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Ports;

public sealed record DetectionDatasetExportResult(
    string OutputRoot,
    int TotalSamples,
    int TrainingCount,
    int ValidationCount,
    int TrainingBoxes,
    int ValidationBoxes,
    int SkippedUnreviewed,
    int SkippedMissingImage,
    int SkippedBoxes,
    IReadOnlyList<string> Classes,
    string DatasetFingerprint);

/// <summary>
/// Exports reviewed detection samples as a YOLO dataset:
/// <c>{train,val}/{images,labels}</c>, empty label files for negatives.
/// </summary>
public interface IDetectionDatasetExportService
{
    Task<Result<DetectionDatasetExportResult>> ExportAsync(
        Guid projectId,
        IReadOnlyList<Guid> galleryIds,
        IReadOnlyDictionary<Guid, GalleryDatasetRole> galleryRoles,
        TrainingDatasetSplitMode splitMode,
        double validationRatio,
        IReadOnlyList<string> classes,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
