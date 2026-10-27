using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Galleries;

public interface ISampleMigrationService
{
    Task<Result> MoveAsync(
        Guid sourceGalleryId,
        Guid targetGalleryId,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<Result<SampleBatchMoveResult>> MoveManyAsync(
        Guid sourceGalleryId,
        Guid targetGalleryId,
        IReadOnlyList<string> fileNames,
        SampleMoveOptions options,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record SampleMoveOptions(bool AutoRenameOnConflict = true);

public sealed class SampleBatchMoveResult
{
    public SampleBatchMoveResult(
        bool succeeded,
        int total,
        int moved,
        int renamed,
        int failed,
        IReadOnlyList<SampleMoveItemResult> items)
    {
        Succeeded = succeeded;
        Total = total;
        Moved = moved;
        Renamed = renamed;
        Failed = failed;
        Items = items;
    }

    public bool Succeeded { get; }

    public int Total { get; }

    public int Moved { get; }

    public int Renamed { get; }

    public int Failed { get; }

    public IReadOnlyList<SampleMoveItemResult> Items { get; }
}

public sealed record SampleMoveItemResult(
    string SourceFileName,
    string? TargetFileName,
    bool Succeeded,
    string? Error);
