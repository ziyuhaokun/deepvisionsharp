using FreeSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class GalleryAnnotationLabelQueryService : IGalleryAnnotationLabelQueryService
{
    private readonly IFreeSql _db;
    private readonly ILogger<GalleryAnnotationLabelQueryService> _logger;

    public GalleryAnnotationLabelQueryService(
        SqliteDb sqliteDb,
        ILogger<GalleryAnnotationLabelQueryService> logger)
    {
        _db = sqliteDb.Instance;
        _logger = logger;
    }

    internal GalleryAnnotationLabelQueryService(
        IFreeSql db,
        ILogger<GalleryAnnotationLabelQueryService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<GalleryAnnotationLabelQueryService>.Instance;
    }

    public async Task<IReadOnlyList<string>> ListDistinctLabelsAsync(
        Guid galleryId,
        CancellationToken cancellationToken = default)
    {
        var rows = await _db.Select<SqlSampleLabelIndex>()
            .Where(index => index.GalleryId == galleryId)
            .ToListAsync(cancellationToken);

        return rows
            .Select(index => index.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(label => label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<IReadOnlyList<string>> ListSampleFileNamesWithLabelAsync(
        Guid galleryId,
        string label,
        GalleryAnnotationLabelMatch match = GalleryAnnotationLabelMatch.Any,
        CancellationToken cancellationToken = default)
    {
        var targetLabel = label.Trim();
        if (string.IsNullOrWhiteSpace(targetLabel))
        {
            _logger.LogWarning(
                "Skipping gallery annotation label query because target label is empty. GalleryId={GalleryId}, Match={Match}",
                galleryId,
                match);
            return [];
        }

        // A classification sample carries exactly one image class, so Any and Only resolve identically.
        var rows = await _db.Select<SqlSampleLabelIndex>()
            .Where(index => index.GalleryId == galleryId)
            .ToListAsync(cancellationToken);

        var matchedFileNames = rows
            .Where(index => string.Equals(index.Label, targetLabel, StringComparison.OrdinalIgnoreCase))
            .Select(index => index.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(fileName => fileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.LogInformation(
            "Resolved gallery label query from SampleLabelIndex. GalleryId={GalleryId}, Label={Label}, Match={Match}, TotalIndexRows={TotalIndexRows}, MatchedFiles={MatchedCount}",
            galleryId,
            targetLabel,
            match,
            rows.Count,
            matchedFileNames.Length);

        return matchedFileNames;
    }
}
