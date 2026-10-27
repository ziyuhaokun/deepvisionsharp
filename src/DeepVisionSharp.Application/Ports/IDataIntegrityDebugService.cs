using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Ports;

public interface IDataIntegrityDebugService
{
    Task<Result<DataIntegrityRepairSummary>> CheckAndRepairAsync(
        CancellationToken cancellationToken = default);
}

public sealed record DataIntegrityRepairSummary(
    int GalleriesScanned,
    int SamplesScanned,
    int AnnotationFilesFound,
    int AnnotationRowsInserted,
    int AnnotationRowsUpdated,
    int SampleFlagsUpdated,
    int GalleryCountsUpdated,
    int LabelIndexesRebuilt,
    int Errors);
