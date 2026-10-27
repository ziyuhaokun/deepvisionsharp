using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Application.Ports;

/// <summary>
/// Clears Shell temp/cache folders and orphaned training run directories
/// that are no longer referenced by any training job row.
/// Implementation must not block the UI thread (disk work runs off-thread).
/// </summary>
public interface IStorageCleanupService
{
    Task<Result<StorageCleanupResult>> CleanAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record StorageCleanupResult(
    int TempCacheEntriesDeleted,
    long TempCacheBytesReclaimed,
    int OrphanTrainingRunsDeleted,
    long OrphanTrainingRunsBytesReclaimed)
{
    public int TotalDeletedEntries => TempCacheEntriesDeleted + OrphanTrainingRunsDeleted;

    public long TotalBytesReclaimed => TempCacheBytesReclaimed + OrphanTrainingRunsBytesReclaimed;
}
