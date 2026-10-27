using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Persistence;

internal sealed class SqliteGalleryRepository : IGalleryRepository
{
    private readonly IFreeSql _db;
    private readonly ILogger<SqliteGalleryRepository> _logger;
    private readonly IProjectFileStore? _fileStore;
    private readonly ProjectOperationGate? _operations;

    public SqliteGalleryRepository(
        SqliteDb sqliteDb,
        IProjectFileStore fileStore,
        ProjectOperationGate operations,
        ILogger<SqliteGalleryRepository> logger)
    {
        _db = sqliteDb.Instance;
        _fileStore = fileStore;
        _operations = operations;
        _logger = logger;
    }

    internal SqliteGalleryRepository(IFreeSql db, ILogger<SqliteGalleryRepository> logger)
    {
        _db = db;
        _fileStore = null;
        _operations = null;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ProjectGallery>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var galleries = await _db.Select<SqlGallery>()
            .Where(gallery => gallery.ProjectId == projectId)
            .OrderByDescending(gallery => gallery.LastUpdatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return galleries
            .Select(gallery => MapGallery(gallery, Array.Empty<SqlSample>()))
            .OrderByDescending(gallery => gallery.LastUpdatedAt)
            .ToArray();
    }

    public async Task<IReadOnlyList<ProjectThumbnailCandidate>> ListThumbnailCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // 封面候选 = 每个项目里「最近更新的有样本图库」的第一个样本（按文件名）。
        // 与旧版直接对全表 Samples 做 ROW_NUMBER 相比，这里先只按图库属性做项目内排名，
        // 再对获胜图库（每项目一个）取 MIN(FileName)，扫描范围收窄到获胜图库，样本量大时不拖慢首屏。
        // GalleryId ASC 作为确定性 tiebreak（同 LastUpdatedUtc 时按图库 ID 稳定收敛）。
        const string sql = """
            WITH project_galleries AS (
                SELECT
                    g.ProjectId,
                    g.Id AS GalleryId,
                    g.LastUpdatedUtc
                FROM Galleries g
                WHERE EXISTS (
                    SELECT 1 FROM Samples s WHERE s.GalleryId = g.Id
                )
            ),
            ranked_galleries AS (
                SELECT
                    ProjectId,
                    GalleryId,
                    ROW_NUMBER() OVER (
                        PARTITION BY ProjectId
                        ORDER BY LastUpdatedUtc DESC, GalleryId ASC
                    ) AS gn
                FROM project_galleries
            ),
            winning_galleries AS (
                SELECT ProjectId, GalleryId
                FROM ranked_galleries
                WHERE gn = 1
            ),
            first_sample AS (
                SELECT
                    s.GalleryId,
                    MIN(s.FileName) AS FileName
                FROM Samples s
                WHERE s.GalleryId IN (SELECT GalleryId FROM winning_galleries)
                GROUP BY s.GalleryId
            )
            SELECT wg.ProjectId, wg.GalleryId, fs.FileName
            FROM winning_galleries wg
            INNER JOIN first_sample fs ON fs.GalleryId = wg.GalleryId
            """;

        var rows = await _db.Ado.QueryAsync<SqlThumbnailCandidate>(sql, cancellationToken).ConfigureAwait(false);
        return rows
            .Select(row => new ProjectThumbnailCandidate(row.ProjectId, row.GalleryId, row.FileName))
            .ToArray();
    }

    public async Task<ProjectGallery?> GetByIdAsync(Guid galleryId, CancellationToken cancellationToken = default)
    {
        var gallery = await _db.Select<SqlGallery>()
            .Where(item => item.Id == galleryId)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (gallery is null)
        {
            return null;
        }

        var samples = await _db.Select<SqlSample>()
            .Where(item => item.GalleryId == galleryId)
            .OrderBy(item => item.FileName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapGallery(gallery, samples);
    }

    public async Task<ProjectGallery?> GetSummaryByIdAsync(Guid galleryId, CancellationToken cancellationToken = default)
    {
        var gallery = await _db.Select<SqlGallery>()
            .Where(item => item.Id == galleryId)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (gallery is null)
        {
            return null;
        }

        return MapGallery(gallery, Array.Empty<SqlSample>());
    }

    public async Task<GallerySamplePage> ListSamplesAsync(
        Guid galleryId,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        if (take <= 0)
        {
            take = 10;
        }

        var query = _db.Select<SqlSample>().Where(sample => sample.GalleryId == galleryId);
        var totalCount = (int)await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var samples = await query
            .OrderBy(sample => sample.FileName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = samples.Select(MapSample).ToArray();
        return new GallerySamplePage(items, totalCount, skip);
    }

    public async Task<IReadOnlyList<string>> ListSampleFileNamesAsync(
        Guid galleryId,
        GallerySampleFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.Select<SqlSample>().Where(sample => sample.GalleryId == galleryId);
        query = ApplySampleFilter(query, filter);
        // Project FileName only — avoid materializing full SqlSample rows for large galleries.
        var fileNames = await query
            .OrderBy(sample => sample.FileName)
            .ToListAsync(sample => sample.FileName, cancellationToken)
            .ConfigureAwait(false);

        return fileNames;
    }

    public async Task<GallerySample?> GetSampleAsync(
        Guid galleryId,
        Guid sampleId,
        CancellationToken cancellationToken = default)
    {
        var sample = await _db.Select<SqlSample>()
            .Where(item => item.GalleryId == galleryId && item.Id == sampleId)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        return sample is null ? null : MapSample(sample);
    }

    public async Task<GallerySample?> GetSampleByFileNameAsync(
        Guid galleryId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return null;
        }

        // Windows file names are case-insensitive and the v8 dedupe already collapses
        // A.jpg/a.jpg, so compare case-insensitively here too; SQLite's BINARY default would
        // otherwise miss the surviving row.
        var safeFileNameLower = safeFileName.ToLowerInvariant();

        var sample = await _db.Select<SqlSample>()
            .Where(item => item.GalleryId == galleryId && item.FileName.ToLower() == safeFileNameLower)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        return sample is null ? null : MapSample(sample);
    }

    public async Task AddSampleAsync(Guid galleryId, GallerySample sample, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        await _db.Insert(new SqlSample
        {
            Id = sample.Id,
            GalleryId = galleryId,
            FileName = sample.FileName,
            IsAnnotated = sample.IsAnnotated,
            HasError = sample.HasError,
            ErrorMessage = sample.ErrorMessage
        }).ExecuteAffrowsAsync(cancellationToken);

        await GalleryStatisticsMaintainer.ApplySampleDeltaAsync(
            _db,
            galleryId,
            sampleDelta: 1,
            annotatedDelta: sample.IsAnnotated ? 1 : 0,
            cancellationToken);
    }

    public async Task UpdateSampleAsync(Guid galleryId, GallerySample sample, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        var previous = await _db.Select<SqlSample>()
            .Where(item => item.Id == sample.Id && item.GalleryId == galleryId)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        await _db.Update<SqlSample>()
            .Set(item => item.FileName, sample.FileName)
            .Set(item => item.IsAnnotated, sample.IsAnnotated)
            .Set(item => item.HasError, sample.HasError)
            .Set(item => item.ErrorMessage, sample.ErrorMessage)
            .Where(item => item.Id == sample.Id && item.GalleryId == galleryId)
            .ExecuteAffrowsAsync(cancellationToken);

        var annotatedDelta = previous is null || previous.IsAnnotated == sample.IsAnnotated
            ? 0
            : sample.IsAnnotated ? 1 : -1;
        await GalleryStatisticsMaintainer.ApplySampleDeltaAsync(
            _db,
            galleryId,
            sampleDelta: 0,
            annotatedDelta,
            cancellationToken);
    }

    public async Task<bool> RemoveSampleByFileNameAsync(
        Guid galleryId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        var safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return false;
        }

        var safeFileNameLower = safeFileName.ToLowerInvariant();

        // Read before delete: the delta needs the previous annotated flag.
        var existing = await _db.Select<SqlSample>()
            .Where(item => item.GalleryId == galleryId && item.FileName.ToLower() == safeFileNameLower)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return false;
        }

        // One transaction: the row delete, the anchor reset and the counter delta must all land or
        // none of them. Committing the delete but not the delta leaves SampleCount permanently one
        // too high, and nothing recomputes it outside the import path.
        using var connectionLease = _db.Ado.MasterPool.Get();
        var connection = connectionLease.Value;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var affected = await _db.Delete<SqlSample>()
                .WithTransaction(transaction)
                .Where(item => item.GalleryId == galleryId && item.FileName.ToLower() == safeFileNameLower)
                .ExecuteAffrowsAsync(cancellationToken);

            if (affected <= 0)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }

            var gallery = await _db.Select<SqlGallery>()
                .WithTransaction(transaction)
                .Where(item => item.Id == galleryId)
                .FirstAsync(cancellationToken)
                .ConfigureAwait(false);
            if (gallery is not null
                && string.Equals(gallery.AnchorFileName, safeFileName, StringComparison.OrdinalIgnoreCase))
            {
                await _db.Update<SqlGallery>()
                    .WithTransaction(transaction)
                    .Set(item => item.AnchorFileName, (string?)null)
                    .Where(item => item.Id == galleryId)
                    .ExecuteAffrowsAsync(cancellationToken);
            }

            await GalleryStatisticsMaintainer.ApplySampleDeltaAsync(
                _db,
                galleryId,
                sampleDelta: -1,
                annotatedDelta: existing.IsAnnotated ? -1 : 0,
                cancellationToken,
                transaction).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The connection may already have rolled back on a failed COMMIT.
            }

            throw;
        }
    }

    public async Task<GallerySample?> GetAdjacentSampleAsync(
        Guid galleryId,
        Guid currentSampleId,
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (offset == 0)
        {
            return await GetSampleAsync(galleryId, currentSampleId, cancellationToken).ConfigureAwait(false);
        }

        var total = (int)await _db.Select<SqlSample>()
            .Where(sample => sample.GalleryId == galleryId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (total == 0)
        {
            return null;
        }

        var currentIndex = await GetSampleIndexAsync(galleryId, currentSampleId, cancellationToken);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        var nextIndex = (currentIndex + offset % total + total) % total;
        var page = await ListSamplesAsync(galleryId, nextIndex, 1, cancellationToken);
        return page.Items.FirstOrDefault();
    }

    public async Task<int> GetSampleIndexAsync(
        Guid galleryId,
        Guid sampleId,
        CancellationToken cancellationToken = default)
    {
        var current = await _db.Select<SqlSample>()
            .Where(sample => sample.GalleryId == galleryId && sample.Id == sampleId)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (current is null)
        {
            return -1;
        }

        return (int)await _db.Select<SqlSample>()
            .Where(sample => sample.GalleryId == galleryId)
            .Where(
                "(FileName < @fileName) OR (FileName = @fileName AND Id < @sampleId)",
                new { fileName = current.FileName, sampleId = current.Id })
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ProjectGallery> CreateAsync(
        Guid projectId,
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        var gallery = new ProjectGallery(
            Guid.NewGuid(),
            projectId,
            name,
            description,
            sampleCount: 0,
            annotatedCount: 0,
            lastUpdatedAt: DateTimeOffset.UtcNow,
            samples: null);

        await SaveAsync(gallery, cancellationToken).ConfigureAwait(false);
        return gallery;
    }

    public async Task<bool> ExistsByNameAsync(
        Guid projectId,
        string name,
        Guid? excludeGalleryId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        // SQLite 默认 BINARY 比较：用 LOWER 对齐 OrdinalIgnoreCase 口径。
        var query = _db.Select<SqlGallery>()
            .Where(item => item.ProjectId == projectId && item.Name.ToLower() == normalized.ToLower());
        if (excludeGalleryId is { } excludeId)
        {
            query = query.Where(item => item.Id != excludeId);
        }

        var exists = await query.AnyAsync(cancellationToken).ConfigureAwait(false);
        return exists;
    }

    public async Task UpdateDetailsAsync(
        Guid galleryId,
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        var affected = await _db.Update<SqlGallery>()
            .Set(item => item.Name, name)
            .Set(item => item.Description, description)
            .Set(item => item.LastUpdatedUtc, DateTime.UtcNow)
            .Where(item => item.Id == galleryId)
            .ExecuteAffrowsAsync(cancellationToken);

        if (affected <= 0)
        {
            _logger.LogWarning("UpdateDetailsAsync found no gallery {GalleryId}", galleryId);
            return;
        }

        _logger.LogInformation("Updated gallery details {GalleryId} {GalleryName}", galleryId, name);
    }

    public async Task SaveAsync(ProjectGallery gallery, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, gallery.ProjectId, cancellationToken).ConfigureAwait(false);
        // Domain ProjectGallery does not carry AnchorFileName; preserve any existing DB value on update.
        var existing = await _db.Select<SqlGallery>()
            .Where(item => item.Id == gallery.Id)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);

        var entity = new SqlGallery
        {
            Id = gallery.Id,
            ProjectId = gallery.ProjectId,
            Name = gallery.Name,
            Description = gallery.Description,
            SampleCount = gallery.SampleCount,
            AnnotatedCount = gallery.AnnotatedCount,
            LastUpdatedUtc = gallery.LastUpdatedAt.UtcDateTime,
            AnchorFileName = existing?.AnchorFileName
        };

        await _db.InsertOrUpdate<SqlGallery>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);

        // Metadata-only / summary callers pass an empty Samples list. Never treat that as
        // "delete every sample" — only reconcile when the caller supplied a sample set.
        if (gallery.Samples.Count == 0)
        {
            await RefreshProjectStatisticsAsync(gallery.ProjectId, cancellationToken);
            _logger.LogInformation(
                "Saved gallery metadata {GalleryId} {GalleryName} (sample reconcile skipped; empty Samples)",
                gallery.Id,
                gallery.Name);
            return;
        }

        var currentSamples = await _db.Select<SqlSample>()
            .Where(item => item.GalleryId == gallery.Id)
            .ToListAsync(cancellationToken);

        var keepIds = gallery.Samples.Select(sample => sample.Id).ToHashSet();
        var staleSamples = currentSamples.Where(sample => !keepIds.Contains(sample.Id)).ToArray();
        foreach (var staleSample in staleSamples)
        {
            // There are no foreign keys in this schema, so dropping the sample row alone leaves its
            // annotations and label-index rows behind; lookups by file name would then find
            // annotations for a sample the gallery no longer lists.
            await _db.Delete<SqlAnnotation>()
                .Where(item => item.GalleryId == staleSample.GalleryId && item.FileName == staleSample.FileName)
                .ExecuteAffrowsAsync(cancellationToken);
            await GallerySampleLabelIndexMaintainer
                .DeleteSampleAsync(_db, staleSample.GalleryId, staleSample.FileName, cancellationToken)
                .ConfigureAwait(false);
            await _db.Delete<SqlSample>().Where(item => item.Id == staleSample.Id).ExecuteAffrowsAsync(cancellationToken);
        }

        var sampleEntities = gallery.Samples.Select(sample => new SqlSample
        {
            Id = sample.Id,
            GalleryId = gallery.Id,
            FileName = sample.FileName,
            IsAnnotated = sample.IsAnnotated,
            HasError = sample.HasError,
            ErrorMessage = sample.ErrorMessage
        }).ToArray();

        await _db.InsertOrUpdate<SqlSample>().SetSource(sampleEntities).ExecuteAffrowsAsync(cancellationToken);

        await RefreshProjectStatisticsAsync(gallery.ProjectId, cancellationToken);
        _logger.LogDebug("Saved gallery {GalleryId} {GalleryName}", gallery.Id, gallery.Name);
    }

    public async Task<bool> DeleteAsync(Guid galleryId, CancellationToken cancellationToken = default)
    {
        var gallery = await _db.Select<SqlGallery>()
            .Where(item => item.Id == galleryId)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (gallery is null)
        {
            return false;
        }

        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, gallery.ProjectId, cancellationToken).ConfigureAwait(false);

        // Excludes imports and training on the same project: an import in flight would keep writing
        // samples into a gallery whose rows this call is about to delete, leaving orphan rows and a
        // recreated image directory behind.
        using var operation = _operations?.EnterGalleryDelete(gallery.ProjectId);

        using var connectionLease = _db.Ado.MasterPool.Get();
        var connection = connectionLease.Value;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _db.Delete<SqlAnnotation>()
                .WithTransaction(transaction)
                .Where(item => item.GalleryId == galleryId)
                .ExecuteAffrowsAsync(cancellationToken);
            await GallerySampleLabelIndexMaintainer.DeleteGalleryAsync(
                _db,
                galleryId,
                cancellationToken,
                transaction);
            await _db.Delete<SqlSample>()
                .WithTransaction(transaction)
                .Where(item => item.GalleryId == galleryId)
                .ExecuteAffrowsAsync(cancellationToken);
            await _db.Delete<SqlGallery>()
                .WithTransaction(transaction)
                .Where(item => item.Id == galleryId)
                .ExecuteAffrowsAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // DB commit is authoritative. Complete derived statistics and disk cleanup even if the UI command is canceled now.
        await RefreshProjectStatisticsAsync(gallery.ProjectId, CancellationToken.None).ConfigureAwait(false);

        // Internal test ctor may omit IProjectFileStore — skip disk cleanup rather than guess a data root.
        if (_fileStore is not null)
        {
            var galleryDirectory = _fileStore.GetGalleryDirectory(gallery.ProjectId, galleryId);
            if (Directory.Exists(galleryDirectory))
            {
                try
                {
                    await Task.Run(() => Directory.Delete(galleryDirectory, recursive: true)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete gallery directory {GalleryDirectory}", galleryDirectory);
                }
            }
        }

        _logger.LogInformation("Deleted gallery {GalleryId}", galleryId);
        return true;
    }

    public async Task<string?> GetAnchorFileNameAsync(Guid galleryId, CancellationToken cancellationToken = default)
    {
        var gallery = await _db.Select<SqlGallery>().Where(item => item.Id == galleryId).FirstAsync(cancellationToken);
        if (gallery is null || string.IsNullOrWhiteSpace(gallery.AnchorFileName))
        {
            return null;
        }

        return gallery.AnchorFileName;
    }

    public async Task SetAnchorFileNameAsync(
        Guid galleryId,
        string? fileName,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureGalleryAsync(_db, galleryId, cancellationToken).ConfigureAwait(false);
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName);
        if (safeFileName is not null)
        {
            var safeFileNameLower = safeFileName.ToLowerInvariant();
            var exists = await _db.Select<SqlSample>()
                .Where(item => item.GalleryId == galleryId && item.FileName.ToLower() == safeFileNameLower)
                .AnyAsync(cancellationToken);
            if (!exists)
            {
                throw new InvalidOperationException("锚点样本不存在。");
            }
        }

        var affected = await _db.Update<SqlGallery>()
            .Set(item => item.AnchorFileName, safeFileName)
            .Where(item => item.Id == galleryId)
            .ExecuteAffrowsAsync(cancellationToken);

        if (affected <= 0)
        {
            throw new InvalidOperationException("图库不存在。");
        }

        _logger.LogInformation(
            "Set gallery {GalleryId} labeling anchor to {AnchorFileName}",
            galleryId,
            safeFileName ?? "(cleared)");
    }

    private Task RefreshProjectStatisticsAsync(Guid projectId, CancellationToken cancellationToken)
        => GalleryStatisticsMaintainer.RefreshProjectAsync(_db, projectId, cancellationToken);

    private static ISelect<SqlSample> ApplySampleFilter(ISelect<SqlSample> query, GallerySampleFilter? filter)
    {
        if (filter is null)
        {
            return query;
        }

        if (filter.IsAnnotated is bool isAnnotated)
        {
            query = query.Where(sample => sample.IsAnnotated == isAnnotated);
        }

        if (filter.HasError is bool hasError)
        {
            query = query.Where(sample => sample.HasError == hasError);
        }

        if (!string.IsNullOrWhiteSpace(filter.FileNamePrefix))
        {
            // StartsWith translates to LIKE 'prefix%', where % and _ are wildcards. Escape them so a
            // prefix containing an underscore (IMG_0) matches literally instead of any single
            // character. SQLite's ESCAPE clause needs a literal backslash in the generated SQL.
            var prefix = filter.FileNamePrefix
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal)
                .Replace("_", "\\_", StringComparison.Ordinal);
            query = query.Where(sample => sample.FileName.StartsWith(prefix));
        }

        return query;
    }

    private static GallerySample MapSample(SqlSample sample)
    {
        return new GallerySample(
            sample.Id,
            sample.GalleryId,
            sample.FileName,
            sample.IsAnnotated,
            sample.HasError,
            sample.ErrorMessage);
    }

    private static ProjectGallery MapGallery(SqlGallery gallery, IEnumerable<SqlSample> samples)
    {
        var sampleRows = samples.Select(sample =>
            new GallerySample(sample.Id, gallery.Id, sample.FileName, sample.IsAnnotated, sample.HasError, sample.ErrorMessage))
            .ToArray();

        return new ProjectGallery(
            gallery.Id,
            gallery.ProjectId,
            gallery.Name,
            gallery.Description,
            gallery.SampleCount,
            gallery.AnnotatedCount,
            new DateTimeOffset(gallery.LastUpdatedUtc, TimeSpan.Zero),
            sampleRows);
    }

    private sealed class SqlThumbnailCandidate
    {
        public Guid ProjectId { get; set; }

        public Guid GalleryId { get; set; }

        public string FileName { get; set; } = string.Empty;
    }
}
