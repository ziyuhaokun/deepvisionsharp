using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Persistence;

internal sealed class SqliteProjectRepository : IProjectRepository
{
    private readonly IFreeSql _db;
    private readonly ILogger<SqliteProjectRepository> _logger;
    private readonly IProjectFileStore? _fileStore;
    private readonly ProjectOperationGate _operations;

    public SqliteProjectRepository(SqliteDb sqliteDb, IProjectFileStore fileStore, ILogger<SqliteProjectRepository> logger, ProjectOperationGate operations)
    {
        _db = sqliteDb.Instance;
        _fileStore = fileStore;
        _logger = logger;
        _operations = operations;
    }

    internal SqliteProjectRepository(IFreeSql db, ILogger<SqliteProjectRepository> logger)
    {
        _db = db;
        _fileStore = null;
        _operations = new ProjectOperationGate();
        _logger = logger;
    }

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
    {
        // 单 SQL 聚合投影：通过 LEFT JOIN 与 GROUP_CONCAT(char(31)) 在数据库端完成标签拼接，
        // 消除全表 ProjectLabels 内存扫描与中间实体分配，单次往返直接产出领域模型。
        const string sql = """
            SELECT
                p.Id,
                p.Name,
                p.Description,
                p.Type,
                p.SampleCount,
                p.Progress,
                p.CreatedUtc,
                p.LastUpdatedUtc,
                GROUP_CONCAT(l.Name, char(31)) AS LabelNamesConcat
            FROM Projects p
            LEFT JOIN ProjectLabels l ON p.Id = l.ProjectId
            GROUP BY p.Id
            ORDER BY p.LastUpdatedUtc DESC;
            """;

        var rows = await _db.Ado.QueryAsync<SqlProjectFlatRow>(sql, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return Array.Empty<Project>();
        }

        return rows.Select(MapFlatProject).ToArray();
    }

    public async Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await _db.Select<SqlProject>()
            .Where(item => item.Id == id)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return null;
        }

        var labels = await _db.Select<SqlProjectLabel>()
            .Where(label => label.ProjectId == id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return MapProject(project, labels);
    }

    public async Task<Project> CreateAsync(string name, string description, string type, CancellationToken cancellationToken = default)
    {
        var project = new Project(
            Guid.NewGuid(),
            name,
            DateTimeOffset.UtcNow,
            description,
            type,
            lastUpdatedAt: DateTimeOffset.UtcNow);

        await SaveAsync(project, cancellationToken).ConfigureAwait(false);
        return project;
    }

    public async Task SaveAsync(Project project, CancellationToken cancellationToken = default)
    {
        ProjectCapabilityCatalog.EnsureActive(project.Type);
        var existing = await GetByIdAsync(project.Id, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            ProjectCapabilityCatalog.EnsureActive(existing.Type);
        }

        var entity = new SqlProject
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
            Type = project.Type,
            SampleCount = project.SampleCount,
            Progress = project.Progress,
            CreatedUtc = project.CreatedAt.UtcDateTime,
            LastUpdatedUtc = project.LastUpdatedAt.UtcDateTime
        };

        await _db.InsertOrUpdate<SqlProject>()
            .SetSource(entity)
            .ExecuteAffrowsAsync(cancellationToken)
            .ConfigureAwait(false);
        _logger.LogDebug("Saved project {ProjectId} {ProjectName}", project.Id, project.Name);
    }

    public async Task UpdateDetailsAsync(
        Guid projectId,
        string name,
        string description,
        CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        var affected = await _db.Update<SqlProject>()
            .Set(item => item.Name, name)
            .Set(item => item.Description, description)
            .Set(item => item.LastUpdatedUtc, DateTime.UtcNow)
            .Where(item => item.Id == projectId)
            .ExecuteAffrowsAsync(cancellationToken)
            .ConfigureAwait(false);

        if (affected <= 0)
        {
            _logger.LogWarning("UpdateDetailsAsync found no project {ProjectId}", projectId);
            return;
        }

        _logger.LogInformation("Updated project details {ProjectId} {ProjectName}", projectId, name);
    }

    public async Task<bool> ExistsByNameAsync(
        string name,
        Guid? excludeProjectId = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        // SQLite 默认 BINARY 比较：用 LOWER 对齐 UI 的 OrdinalIgnoreCase 口径。
        var query = _db.Select<SqlProject>()
            .Where(project => project.Name.ToLower() == normalized.ToLower());
        if (excludeProjectId is { } excludeId)
        {
            query = query.Where(project => project.Id != excludeId);
        }

        var exists = await query.AnyAsync(cancellationToken).ConfigureAwait(false);
        return exists;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var operation = _operations.EnterDelete(id);
        if (await _db.Select<SqlTrainingJob>().Where(job => job.ProjectId == id
            && (job.Status == "Preparing" || job.Status == "Running"))
            .AnyAsync(cancellationToken).ConfigureAwait(false))
            throw new ProjectBusyException("项目有正在准备或运行的训练任务，请先停止训练后再删除。");
        var project = await _db.Select<SqlProject>()
            .Where(item => item.Id == id)
            .FirstAsync(cancellationToken)
            .ConfigureAwait(false);
        if (project is null)
        {
            return false;
        }

        ProjectCapabilityCatalog.EnsureActive(project.Type);

        using var connectionLease = _db.Ado.MasterPool.Get();
        var connection = connectionLease.Value;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read inside the transaction. Reading before it meant a gallery created in between was
            // not in the list, so its child rows survived while the parent row was deleted.
            var galleryIds = await _db.Select<SqlGallery>()
                .WithTransaction(transaction)
                .Where(gallery => gallery.ProjectId == id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var gallery in galleryIds)
            {
                await _db.Delete<SqlAnnotation>()
                    .WithTransaction(transaction)
                    .Where(item => item.GalleryId == gallery.Id)
                    .ExecuteAffrowsAsync(cancellationToken);
                await GallerySampleLabelIndexMaintainer.DeleteGalleryAsync(
                    _db,
                    gallery.Id,
                    cancellationToken,
                    transaction);
                await _db.Delete<SqlSample>()
                    .WithTransaction(transaction)
                    .Where(item => item.GalleryId == gallery.Id)
                    .ExecuteAffrowsAsync(cancellationToken);
            }

            await _db.Delete<SqlGallery>()
                .WithTransaction(transaction)
                .Where(item => item.ProjectId == id)
                .ExecuteAffrowsAsync(cancellationToken);
            await _db.Delete<SqlModel>()
                .WithTransaction(transaction)
                .Where(item => item.ProjectId == id)
                .ExecuteAffrowsAsync(cancellationToken);
            await _db.Delete<SqlTrainingJob>()
                .WithTransaction(transaction)
                .Where(item => item.ProjectId == id)
                .ExecuteAffrowsAsync(cancellationToken);
            await _db.Delete<SqlProjectLabel>()
                .WithTransaction(transaction)
                .Where(item => item.ProjectId == id)
                .ExecuteAffrowsAsync(cancellationToken);
            await _db.Delete<SqlProject>()
                .WithTransaction(transaction)
                .Where(item => item.Id == id)
                .ExecuteAffrowsAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                // Roll back with no token: if the failure was a cancellation, passing the cancelled
                // token would throw from the rollback and replace the original exception.
                await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The provider may already have rolled back a failed COMMIT.
            }

            throw;
        }

        // Disk cleanup only after DB commit so a failed delete does not leave a half-deleted project.
        // Internal test ctor may omit IProjectFileStore — skip disk cleanup rather than guess a data root.
        if (_fileStore is not null)
        {
            var projectDirectory = _fileStore.GetProjectDirectory(id);
            if (Directory.Exists(projectDirectory))
            {
                try
                {
                    await Task.Run(() => Directory.Delete(projectDirectory, recursive: true)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete project directory {ProjectDirectory}", projectDirectory);
                }
            }
        }

        _logger.LogInformation("Deleted project {ProjectId}", id);
        return true;
    }

    private static Project MapFlatProject(SqlProjectFlatRow row)
    {
        var labelNames = string.IsNullOrWhiteSpace(row.LabelNamesConcat)
            ? Array.Empty<string>()
            : row.LabelNamesConcat
                .Split((char)31, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        var createdUtc = row.CreatedUtc is { } created && created > DateTime.MinValue
            ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
            : DateTime.SpecifyKind(row.LastUpdatedUtc, DateTimeKind.Utc);

        return new Project(
            row.Id,
            row.Name,
            createdAt: new DateTimeOffset(createdUtc, TimeSpan.Zero),
            row.Description,
            row.Type,
            row.SampleCount,
            labelCount: labelNames.Length,
            row.Progress,
            lastUpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(row.LastUpdatedUtc, DateTimeKind.Utc), TimeSpan.Zero),
            labels: labelNames);
    }

    private static Project MapProject(SqlProject project, IEnumerable<SqlProjectLabel> labels)
    {
        var labelNames = labels
            .Select(label => label.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var createdUtc = project.CreatedUtc is { } created && created > DateTime.MinValue
            ? DateTime.SpecifyKind(created, DateTimeKind.Utc)
            : DateTime.SpecifyKind(project.LastUpdatedUtc, DateTimeKind.Utc);

        return new Project(
            project.Id,
            project.Name,
            createdAt: new DateTimeOffset(createdUtc, TimeSpan.Zero),
            project.Description,
            project.Type,
            project.SampleCount,
            labelCount: labelNames.Length,
            project.Progress,
            lastUpdatedAt: new DateTimeOffset(DateTime.SpecifyKind(project.LastUpdatedUtc, DateTimeKind.Utc), TimeSpan.Zero),
            labels: labelNames);
    }

    private sealed class SqlProjectFlatRow
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string Type { get; set; } = string.Empty;

        public int SampleCount { get; set; }

        public double Progress { get; set; }

        public DateTime? CreatedUtc { get; set; }

        public DateTime LastUpdatedUtc { get; set; }

        public string? LabelNamesConcat { get; set; }
    }
}
