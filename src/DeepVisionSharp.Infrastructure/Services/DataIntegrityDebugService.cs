using System.Text.Json;
using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class DataIntegrityDebugService : IDataIntegrityDebugService
{
    private readonly IFreeSql _db;
    private readonly IProjectFileStore _fileStore;
    private readonly ILogger<DataIntegrityDebugService> _logger;

    public DataIntegrityDebugService(
        SqliteDb sqliteDb,
        IProjectFileStore fileStore,
        ILogger<DataIntegrityDebugService> logger)
    {
        _db = sqliteDb.Instance;
        _fileStore = fileStore;
        _logger = logger;
    }

    public async Task<Result<DataIntegrityRepairSummary>> CheckAndRepairAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var activeProjects = (await _db.Select<SqlProject>().ToListAsync(cancellationToken))
                .Where(project => ProjectCapabilityCatalog.IsActive(project.Type))
                .Select(project => project.Id).ToHashSet();
            var galleries = await _db.Select<SqlGallery>()
                .OrderBy(gallery => gallery.ProjectId)
                .OrderBy(gallery => gallery.Name)
                .ToListAsync(cancellationToken);

            var samples = await _db.Select<SqlSample>()
                .OrderBy(sample => sample.GalleryId)
                .OrderBy(sample => sample.FileName)
                .ToListAsync(cancellationToken);

            var sampleLookup = samples
                .GroupBy(sample => sample.GalleryId)
                .ToDictionary(group => group.Key, group => group.ToArray());

            var annotationRowsInserted = 0;
            var annotationRowsUpdated = 0;
            var sampleFlagsUpdated = 0;
            var galleryCountsUpdated = 0;
            var labelIndexesRebuilt = 0;
            var annotationFilesFound = 0;
            var errors = 0;

            foreach (var gallery in galleries)
            {
                if (!activeProjects.Contains(gallery.ProjectId))
                {
                    continue;
                }
                cancellationToken.ThrowIfCancellationRequested();
                var gallerySamples = sampleLookup.TryGetValue(gallery.Id, out var value) ? value : [];
                var annotatedCount = 0;

                foreach (var sample in gallerySamples)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var imagePath = _fileStore.ResolveSampleImagePath(gallery.ProjectId, gallery.Id, sample.FileName);
                    string? documentJson = null;
                    var hasMeaningfulAnnotation = false;

                    var existing = await _db.Select<SqlAnnotation>()
                        .Where(row => row.GalleryId == gallery.Id && row.FileName == sample.FileName)
                        .FirstAsync(cancellationToken);

                    var content = await SampleAnnotationFiles.ReadAsync(imagePath, existing?.DocumentJson, cancellationToken);
                    var hasAnnotationFile = content.FilePath is not null;

                    if (hasAnnotationFile)
                    {
                        annotationFilesFound++;
                        documentJson = content.Json!;
                        hasMeaningfulAnnotation = HasMeaningfulAnnotation(documentJson);

                        if (existing is null)
                        {
                            var (width, height) = ReadDimensions(documentJson);
                            await _db.Insert(new SqlAnnotation
                            {
                                GalleryId = gallery.Id,
                                FileName = sample.FileName,
                                Width = width,
                                Height = height,
                                ImageData = null,
                                DocumentJson = documentJson
                            }).ExecuteAffrowsAsync(cancellationToken);
                            annotationRowsInserted++;
                        }
                        else if (!string.Equals(existing.DocumentJson, documentJson, StringComparison.Ordinal))
                        {
                            var (width, height) = ReadDimensions(documentJson);
                            await _db.Update<SqlAnnotation>()
                                .Set(row => row.Width, width)
                                .Set(row => row.Height, height)
                                .Set(row => row.DocumentJson, documentJson)
                                .Where(row => row.Id == existing.Id)
                                .ExecuteAffrowsAsync(cancellationToken);
                            annotationRowsUpdated++;
                        }

                        await GallerySampleLabelIndexMaintainer.ReplaceLabelsFromDocumentAsync(
                            _db,
                            gallery.Id,
                            sample.FileName,
                            documentJson,
                            cancellationToken);
                        labelIndexesRebuilt++;
                    }
                    else if (existing is not null && !string.IsNullOrWhiteSpace(existing.DocumentJson))
                    {
                        // No sidecar: trust SQLite DocumentJson. Never wipe index/flags for DB-only annotations.
                        documentJson = existing.DocumentJson;
                        hasMeaningfulAnnotation = HasMeaningfulAnnotation(documentJson);
                        await GallerySampleLabelIndexMaintainer.ReplaceLabelsFromDocumentAsync(
                            _db,
                            gallery.Id,
                            sample.FileName,
                            documentJson,
                            cancellationToken);
                        labelIndexesRebuilt++;
                    }
                    else
                    {
                        // Neither file nor DB holds annotation content.
                        await GallerySampleLabelIndexMaintainer.DeleteSampleAsync(
                            _db,
                            gallery.Id,
                            sample.FileName,
                            cancellationToken);
                    }

                    if (sample.IsAnnotated != hasMeaningfulAnnotation)
                    {
                        await _db.Update<SqlSample>()
                            .Set(row => row.IsAnnotated, hasMeaningfulAnnotation)
                            .Where(row => row.Id == sample.Id)
                            .ExecuteAffrowsAsync(cancellationToken);
                        sampleFlagsUpdated++;
                    }

                    if (hasMeaningfulAnnotation)
                    {
                        annotatedCount++;
                    }
                }

                if (gallery.SampleCount != gallerySamples.Length || gallery.AnnotatedCount != annotatedCount)
                {
                    await _db.Update<SqlGallery>()
                        .Set(row => row.SampleCount, gallerySamples.Length)
                        .Set(row => row.AnnotatedCount, annotatedCount)
                        .Set(row => row.LastUpdatedUtc, DateTime.UtcNow)
                        .Where(row => row.Id == gallery.Id)
                        .ExecuteAffrowsAsync(cancellationToken);
                    galleryCountsUpdated++;
                }
            }

            var summary = new DataIntegrityRepairSummary(
                galleries.Count,
                samples.Count,
                annotationFilesFound,
                annotationRowsInserted,
                annotationRowsUpdated,
                sampleFlagsUpdated,
                galleryCountsUpdated,
                labelIndexesRebuilt,
                errors);

            _logger.LogInformation(
                "Data integrity repair completed. Galleries={Galleries}, Samples={Samples}, AnnotationFilesFound={AnnotationFilesFound}, AnnotationRowsInserted={AnnotationRowsInserted}, AnnotationRowsUpdated={AnnotationRowsUpdated}, SampleFlagsUpdated={SampleFlagsUpdated}, GalleryCountsUpdated={GalleryCountsUpdated}, LabelIndexesRebuilt={LabelIndexesRebuilt}, Errors={Errors}",
                summary.GalleriesScanned,
                summary.SamplesScanned,
                summary.AnnotationFilesFound,
                summary.AnnotationRowsInserted,
                summary.AnnotationRowsUpdated,
                summary.SampleFlagsUpdated,
                summary.GalleryCountsUpdated,
                summary.LabelIndexesRebuilt,
                summary.Errors);

            return Result<DataIntegrityRepairSummary>.Success(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data integrity repair failed");
            return Result<DataIntegrityRepairSummary>.Failure($"数据检查修复失败: {ex.Message}");
        }
    }

    private static bool HasMeaningfulAnnotation(string? documentJson) =>
        !string.IsNullOrWhiteSpace(AnnotationDocumentLabelReader.ReadImageClass(documentJson));

    private static (double Width, double Height) ReadDimensions(string? documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            return (0, 0);
        }

        try
        {
            using var document = JsonDocument.Parse(documentJson);
            var root = document.RootElement;
            var width = root.TryGetProperty("imageWidth", out var widthElement) && widthElement.TryGetDouble(out var parsedWidth)
                ? parsedWidth
                : 0;
            var height = root.TryGetProperty("imageHeight", out var heightElement) && heightElement.TryGetDouble(out var parsedHeight)
                ? parsedHeight
                : 0;
            return (width, height);
        }
        catch (JsonException)
        {
            return (0, 0);
        }
    }
}
