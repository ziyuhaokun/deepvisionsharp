using FreeSql;

using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services.Transfer;

/// <summary>
/// Reads package annotation rows on demand.
/// </summary>
/// <remarks>
/// A package stores each image as Base64 inside <see cref="SqlAnnotation.ImageData"/>, and a
/// <see cref="string"/> is UTF-16: a 1.5 MB JPEG occupies roughly 2.7 MB once materialized.
/// Loading a whole gallery (or even a page of it) therefore costs hundreds of megabytes, so the
/// import loops read exactly one row per sample and let it go. The (GalleryId, FileName) unique
/// index keeps the per-sample lookup cheap; the case-insensitive fallback only runs when the
/// exact match misses.
/// </remarks>
internal static class TransferAnnotationReader
{
    public static async Task<SqlAnnotation?> FindAsync(
        IFreeSql db,
        Guid galleryId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var row = await db.Select<SqlAnnotation>()
            .Where(item => item.GalleryId == galleryId && item.FileName == fileName)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is not null)
        {
            return row;
        }

        // Legacy packages may differ in case between Samples and Annotations rows. The BINARY
        // index cannot serve this comparison, but it only runs on a miss.
        var lowered = fileName.ToLower();
        return await db.Select<SqlAnnotation>()
            .Where(item => item.GalleryId == galleryId && item.FileName.ToLower() == lowered)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
