namespace DeepVisionSharp.Application.Ports;

public interface ISampleFileMover
{
    Task<SampleFileMoveResult> MoveAsync(
        Guid projectId,
        Guid sourceGalleryId,
        Guid targetGalleryId,
        string fileName,
        CancellationToken cancellationToken = default);
}

public sealed record SampleFileMoveResult(SampleFileMoveOutcome Outcome, string? ErrorMessage = null);

public enum SampleFileMoveOutcome
{
    Moved,
    SourceImageMissing,
    TargetImageExists,
    Failed
}
