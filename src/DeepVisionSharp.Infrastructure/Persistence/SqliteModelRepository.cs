using FreeSql;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Persistence;

internal sealed class SqliteModelRepository : IModelRepository
{
    private readonly IFreeSql _db;
    private readonly ProjectOperationGate? _operations;
    private readonly ILogger<SqliteModelRepository> _logger;

    public SqliteModelRepository(SqliteDb sqliteDb, ProjectOperationGate operations, ILogger<SqliteModelRepository> logger)
    {
        _db = sqliteDb.Instance;
        _operations = operations;
        _logger = logger;
    }

    internal SqliteModelRepository(IFreeSql db, ILogger<SqliteModelRepository> logger, ProjectOperationGate? operations = null)
    {
        _db = db;
        _operations = operations;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TrainedModel>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var models = await _db.Select<SqlModel>()
            .Where(model => model.ProjectId == projectId)
            .OrderByDescending(model => model.CreatedUtc)
            .ToListAsync(cancellationToken);

        return models.Select(MapModel).ToArray();
    }

    public async Task<TrainedModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var model = await _db.Select<SqlModel>().Where(item => item.Id == id).FirstAsync(cancellationToken);
        return model is null ? null : MapModel(model);
    }

    public async Task SaveAsync(Guid projectId, TrainedModel model, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        var entity = new SqlModel
        {
            Id = model.Id,
            ProjectId = projectId,
            Name = model.Name,
            ModelType = model.ModelType,
            Status = TrainedModelStatuses.ToPersistedText(model.Status),
            Note = model.Note,
            ClassesJson = JsonSerializer.Serialize(model.Classes),
            CheckpointPath = model.CheckpointPath,
            CreatedUtc = model.CreatedAt.UtcDateTime,
            MaxDetections = model.MaxDetections
        };

        await _db.InsertOrUpdate<SqlModel>().SetSource(entity).ExecuteAffrowsAsync(cancellationToken);
        _logger.LogDebug("Saved model {ModelId} {Name}", model.Id, model.Name);
    }

    public async Task<bool> ExistsWithNameAsync(
        Guid projectId,
        string name,
        Guid? excludeModelId = null,
        CancellationToken cancellationToken = default)
    {
        // SQLite's default BINARY collation is case-sensitive; projects and galleries compare with
        // LOWER to match the case-insensitive naming the UI enforces. Do the same here so a project
        // cannot hold both "ModelA" and "modela".
        var normalized = name.Trim().ToLowerInvariant();
        var query = _db.Select<SqlModel>()
            .Where(item => item.ProjectId == projectId)
            .Where(item => item.Name.ToLower() == normalized);

        if (excludeModelId.HasValue)
        {
            query = query.Where(item => item.Id != excludeModelId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default)
    {
        await SqlProjectExecutionGuard.EnsureProjectAsync(_db, projectId, cancellationToken).ConfigureAwait(false);
        using var operation = _operations?.EnterGalleryDelete(projectId);
        var affectedRows = await _db.Delete<SqlModel>()
            .Where(item => item.ProjectId == projectId && item.Id == id)
            .ExecuteAffrowsAsync(cancellationToken);

        if (affectedRows > 0)
        {
            _logger.LogInformation("Deleted model {ModelId}", id);
            return true;
        }

        return false;
    }

    private static TrainedModel MapModel(SqlModel model)
    {
        var classes = JsonSerializer.Deserialize<List<string>>(model.ClassesJson) ?? new List<string>();
        return new TrainedModel(
            model.Id,
            model.ProjectId,
            model.Name,
            model.ModelType,
            TrainedModelStatuses.ParsePersistedText(model.Status),
            model.Note,
            classes,
            model.CheckpointPath,
            new DateTimeOffset(model.CreatedUtc, TimeSpan.Zero),
            model.MaxDetections);
    }
}
