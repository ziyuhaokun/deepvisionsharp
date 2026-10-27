using FreeSql;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ProjectLabelStatisticsQueryService : IProjectLabelStatisticsQueryService
{
    private readonly IFreeSql _db;

    public ProjectLabelStatisticsQueryService(SqliteDb sqliteDb)
    {
        _db = sqliteDb.Instance;
    }

    public Task<ProjectLabelStatisticsSnapshot> GetStatisticsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default)
        => QueryAsync(
            (index, gallery) => gallery.ProjectId == projectId,
            cancellationToken);

    public Task<ProjectLabelStatisticsSnapshot> GetGalleryStatisticsAsync(
        Guid galleryId,
        CancellationToken cancellationToken = default)
        => QueryAsync(
            (index, gallery) => index.GalleryId == galleryId,
            cancellationToken);

    private async Task<ProjectLabelStatisticsSnapshot> QueryAsync(
        System.Linq.Expressions.Expression<Func<SqlSampleLabelIndex, SqlGallery, bool>> predicate,
        CancellationToken cancellationToken)
    {
        var indexRows = await _db.Select<SqlSampleLabelIndex, SqlGallery>()
            .InnerJoin((index, gallery) => index.GalleryId == gallery.Id)
            .Where(predicate)
            .ToListAsync((index, gallery) => new LabelIndexProjection(
                index.GalleryId,
                gallery.Name,
                index.Label), cancellationToken);

        if (indexRows.Count == 0)
        {
            return new ProjectLabelStatisticsSnapshot([], []);
        }

        var labelCounts = indexRows
            .GroupBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ProjectLabelCountRow(group.Key, group.Count()))
            .OrderByDescending(row => row.SampleCount)
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Same case-insensitive grouping as the project totals above, otherwise a class written as
        // "ok" and as "OK" shows one row at project level and two per gallery.
        var galleryLabelCounts = indexRows
            .GroupBy(
                row => (row.GalleryId, Label: row.Label.ToUpperInvariant()),
                new GalleryLabelKeyComparer())
            .Select(group => new ProjectGalleryLabelCountRow(
                group.Key.GalleryId,
                group.First().GalleryName,
                group.Key.Label,
                group.Count()))
            .OrderBy(row => row.GalleryName, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(row => row.SampleCount)
            .ThenBy(row => row.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProjectLabelStatisticsSnapshot(labelCounts, galleryLabelCounts);
    }

    private sealed record LabelIndexProjection(Guid GalleryId, string GalleryName, string Label);

    /// <summary>
    /// Groups by (gallery, upper-cased label). The display label is the upper-cased form, matching
    /// the project-level rows so both levels agree on what counts as one class.
    /// </summary>
    private sealed class GalleryLabelKeyComparer : IEqualityComparer<(Guid GalleryId, string Label)>
    {
        public bool Equals((Guid GalleryId, string Label) x, (Guid GalleryId, string Label) y) =>
            x.GalleryId == y.GalleryId
            && string.Equals(x.Label, y.Label, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid GalleryId, string Label) obj) =>
            HashCode.Combine(obj.GalleryId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Label ?? string.Empty));
    }
}
