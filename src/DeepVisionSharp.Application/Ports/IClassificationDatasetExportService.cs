using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Ports;

public sealed record ClassificationDatasetExportResult(
    string OutputDirectory,
    int TotalSamples,
    int ExportedCount,
    int TrainingCount,
    int ValidationCount,
    int SkippedMissingImage,
    int SkippedMissingClass,
    IReadOnlyList<string> Classes,
    string DatasetFingerprint);

public interface IClassificationDatasetExportService
{
    Task<Result<ClassificationDatasetExportResult>> ExportAsync(
        Guid projectId,
        IReadOnlyList<Guid> galleryIds,
        IReadOnlyDictionary<Guid, GalleryDatasetRole> galleryRoles,
        TrainingDatasetSplitMode splitMode,
        double validationRatio,
        IReadOnlyList<string> classes,
        string outputRoot,
        CancellationToken cancellationToken = default);
}
