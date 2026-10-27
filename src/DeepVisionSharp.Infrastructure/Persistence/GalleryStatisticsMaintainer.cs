using System.Data;
using System.Data.Common;
using FreeSql;

namespace DeepVisionSharp.Infrastructure.Persistence;

/// <summary>Refresh derived counts in the same transaction as sample/annotation changes.</summary>
internal static class GalleryStatisticsMaintainer
{
    /// <summary>
    /// Applies an incremental sample/annotation delta instead of recounting the gallery.
    /// Per-sample writes used to run two full COUNTs (import, labeling save, batch write-back),
    /// which made those flows O(n²). The gallery timestamp is still touched so ordering matches
    /// the previous full-refresh behavior; project counts are re-summed from gallery counters
    /// (O(galleries), not O(samples)). Use <see cref="RefreshGalleryAsync"/> for bulk reconcile.
    /// </summary>
    internal static async Task ApplySampleDeltaAsync(
        IFreeSql db,
        Guid galleryId,
        int sampleDelta,
        int annotatedDelta,
        CancellationToken token,
        DbTransaction? transaction = null)
    {
        // SetRaw: FreeSql 3.2 has no "column = column + delta" expression overload, and a
        // read-modify-write would lose updates when two saves overlap on different connections.
        var updated = await db.Update<SqlGallery>().WithTransaction(transaction)
            .SetRaw(
                "\"SampleCount\" = \"SampleCount\" + @sampleDelta, " +
                "\"AnnotatedCount\" = \"AnnotatedCount\" + @annotatedDelta, " +
                "\"LastUpdatedUtc\" = @now",
                new { sampleDelta, annotatedDelta, now = DateTime.UtcNow })
            .Where(item => item.Id == galleryId)
            .ExecuteAffrowsAsync(token).ConfigureAwait(false);
        if (updated <= 0)
        {
            return;
        }

        var gallery = await db.Select<SqlGallery>().WithTransaction(transaction)
            .Where(item => item.Id == galleryId).FirstAsync(token).ConfigureAwait(false);
        if (gallery is null)
        {
            return;
        }

        await RefreshProjectAsync(db, gallery.ProjectId, token, transaction).ConfigureAwait(false);
    }

    internal static async Task RefreshGalleryAsync(
        IFreeSql db, Guid galleryId, CancellationToken token, DbTransaction? transaction = null)
    {
        var gallery = await db.Select<SqlGallery>().WithTransaction(transaction)
            .Where(item => item.Id == galleryId).FirstAsync(token).ConfigureAwait(false);
        if (gallery is null) return;

        var count = (int)await db.Select<SqlSample>().WithTransaction(transaction)
            .Where(item => item.GalleryId == galleryId).CountAsync(token).ConfigureAwait(false);
        var annotated = (int)await db.Select<SqlSample>().WithTransaction(transaction)
            .Where(item => item.GalleryId == galleryId && item.IsAnnotated).CountAsync(token).ConfigureAwait(false);
        await db.Update<SqlGallery>().WithTransaction(transaction)
            .Set(item => item.SampleCount, count)
            .Set(item => item.AnnotatedCount, annotated)
            .Set(item => item.LastUpdatedUtc, DateTime.UtcNow)
            .Where(item => item.Id == galleryId).ExecuteAffrowsAsync(token).ConfigureAwait(false);
        await RefreshProjectAsync(db, gallery.ProjectId, token, transaction).ConfigureAwait(false);
    }

    /// <summary>
    /// Recomputes the project totals from its galleries.
    /// </summary>
    /// <remarks>
    /// The gallery counters are already written by <see cref="ApplySampleDeltaAsync"/> as relative
    /// increments, but this method has to read them to sum them. Without a transaction two
    /// concurrent callers can interleave read and write and the later writer stores a stale sum,
    /// leaving the project count permanently low. Callers that already hold a transaction get
    /// consistency for free; standalone callers get one here.
    /// </remarks>
    internal static async Task RefreshProjectAsync(
        IFreeSql db, Guid projectId, CancellationToken token, DbTransaction? transaction = null)
    {
        if (transaction is not null)
        {
            await RefreshProjectCoreAsync(db, projectId, token, transaction).ConfigureAwait(false);
            return;
        }

        using var connectionLease = db.Ado.MasterPool.Get();
        var connection = connectionLease.Value;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
        }

        await using var ownTransaction = await connection.BeginTransactionAsync(token).ConfigureAwait(false);
        try
        {
            await RefreshProjectCoreAsync(db, projectId, token, ownTransaction).ConfigureAwait(false);
            await ownTransaction.CommitAsync(token).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await ownTransaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Already rolled back by the provider.
            }

            throw;
        }
    }

    private static async Task RefreshProjectCoreAsync(
        IFreeSql db, Guid projectId, CancellationToken token, DbTransaction transaction)
    {
        var galleries = await db.Select<SqlGallery>().WithTransaction(transaction)
            .Where(item => item.ProjectId == projectId).ToListAsync(token).ConfigureAwait(false);
        var count = galleries.Sum(item => item.SampleCount);
        var annotated = galleries.Sum(item => item.AnnotatedCount);
        await db.Update<SqlProject>().WithTransaction(transaction)
            .Set(item => item.SampleCount, count)
            .Set(item => item.Progress, count > 0 ? (double)annotated / count : 0)
            .Set(item => item.LastUpdatedUtc, DateTime.UtcNow)
            .Where(item => item.Id == projectId).ExecuteAffrowsAsync(token).ConfigureAwait(false);
    }
}
