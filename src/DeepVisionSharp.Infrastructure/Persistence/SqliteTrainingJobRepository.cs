using Microsoft.Extensions.Logging;
using System.Text.Json;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Persistence;

internal sealed class SqliteTrainingJobRepository : ITrainingJobRepository
{
    private readonly SqliteDb _sqliteDb;
    private readonly ILogger<SqliteTrainingJobRepository> _logger;

    public SqliteTrainingJobRepository(SqliteDb sqliteDb, ILogger<SqliteTrainingJobRepository> logger)
    {
        _sqliteDb = sqliteDb;
        _logger = logger;
    }

    public async Task<TrainingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _sqliteDb.Instance
            .Select<SqlTrainingJob>()
            .Where(item => item.Id == id)
            .FirstAsync(cancellationToken);

        return entity is null ? null : MapFromEntity(entity);
    }

    private TrainingJobStatus ParsePersistedStatus(Guid jobId, string persistedStatus)
    {
        if (Enum.TryParse<TrainingJobStatus>(persistedStatus, ignoreCase: true, out var status))
        {
            return status;
        }

        _logger.LogWarning(
            "Training job {JobId} has invalid persisted status '{PersistedStatus}', treating as Failed",
            jobId,
            persistedStatus);
        return TrainingJobStatus.Failed;
    }

    public async Task<IReadOnlyList<TrainingJob>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var entities = await _sqliteDb.Instance.Select<SqlTrainingJob>().ToListAsync(cancellationToken);
        return entities.Select(MapFromEntity).ToList();
    }

    public async Task<IReadOnlyList<TrainingJob>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var entities = await _sqliteDb.Instance
            .Select<SqlTrainingJob>()
            .Where(item => item.ProjectId == projectId)
            .ToListAsync(cancellationToken);

        return entities.Select(MapFromEntity).ToList();
    }

    public async Task<IReadOnlyList<TrainingJob>> GetRunningAsync(CancellationToken cancellationToken = default)
    {
        return await GetByStatusesAsync([TrainingJobStatus.Running], cancellationToken);
    }

    public async Task<IReadOnlyList<TrainingJob>> GetByStatusesAsync(
        IReadOnlyList<TrainingJobStatus> statuses,
        CancellationToken cancellationToken = default)
    {
        if (statuses.Count == 0)
        {
            return [];
        }

        var statusNames = statuses
            .Select(status => status.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // FreeSql/SQLite cannot translate local-array .Contains(string) (NotSupportedException at
        // hosted recovery startup). Keep filtering in SQL via equality/OR on the small status enum.
        var select = ApplyStatusInFilter(_sqliteDb.Instance.Select<SqlTrainingJob>(), statusNames);
        var entities = await select.ToListAsync(cancellationToken);

        return entities.Select(MapFromEntity).ToList();
    }

    /// <summary>
    /// Builds a FreeSql-translatable Status filter. Locals are captured so expressions stay simple equality/OR trees.
    /// </summary>
    private static FreeSql.ISelect<SqlTrainingJob> ApplyStatusInFilter(
        FreeSql.ISelect<SqlTrainingJob> select,
        string[] statusNames)
    {
        // TrainingJobStatus has 6 values; callers typically pass Preparing+Running only.
        return statusNames.Length switch
        {
            1 => Filter1(select, statusNames[0]),
            2 => Filter2(select, statusNames[0], statusNames[1]),
            3 => Filter3(select, statusNames[0], statusNames[1], statusNames[2]),
            4 => Filter4(select, statusNames[0], statusNames[1], statusNames[2], statusNames[3]),
            5 => Filter5(select, statusNames[0], statusNames[1], statusNames[2], statusNames[3], statusNames[4]),
            _ => Filter6(
                select,
                statusNames[0],
                statusNames[1],
                statusNames[2],
                statusNames[3],
                statusNames[4],
                statusNames[5]),
        };

        static FreeSql.ISelect<SqlTrainingJob> Filter1(FreeSql.ISelect<SqlTrainingJob> q, string a)
            => q.Where(e => e.Status == a);

        static FreeSql.ISelect<SqlTrainingJob> Filter2(FreeSql.ISelect<SqlTrainingJob> q, string a, string b)
            => q.Where(e => e.Status == a || e.Status == b);

        static FreeSql.ISelect<SqlTrainingJob> Filter3(FreeSql.ISelect<SqlTrainingJob> q, string a, string b, string c)
            => q.Where(e => e.Status == a || e.Status == b || e.Status == c);

        static FreeSql.ISelect<SqlTrainingJob> Filter4(
            FreeSql.ISelect<SqlTrainingJob> q, string a, string b, string c, string d)
            => q.Where(e => e.Status == a || e.Status == b || e.Status == c || e.Status == d);

        static FreeSql.ISelect<SqlTrainingJob> Filter5(
            FreeSql.ISelect<SqlTrainingJob> q, string a, string b, string c, string d, string e)
            => q.Where(x => x.Status == a || x.Status == b || x.Status == c || x.Status == d || x.Status == e);

        static FreeSql.ISelect<SqlTrainingJob> Filter6(
            FreeSql.ISelect<SqlTrainingJob> q, string a, string b, string c, string d, string e, string f)
            => q.Where(x =>
                x.Status == a || x.Status == b || x.Status == c || x.Status == d || x.Status == e || x.Status == f);
    }

    public async Task<bool> SaveIfStatusAsync(
        TrainingJob job,
        IReadOnlyCollection<TrainingJobStatus> allowedCurrent,
        CancellationToken cancellationToken = default)
    {
        var names = allowedCurrent.Select(status => status.ToString()).Distinct(StringComparer.Ordinal).ToList();
        if (names.Count == 0 || names.Count > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(allowedCurrent), "需要 1 到 4 个允许的当前状态。");
        }

        if (job.ProjectId is Guid projectId)
        {
            await SqlProjectExecutionGuard.EnsureProjectAsync(_sqliteDb.Instance, projectId, cancellationToken).ConfigureAwait(false);
        }

        // FreeSql cannot translate local-array Contains; pad to four equality terms instead.
        while (names.Count < 4)
        {
            names.Add(names[0]);
        }

        var (a, b, c, d) = (names[0], names[1], names[2], names[3]);
        var id = job.Id;
        var affected = await _sqliteDb.Instance
            .Update<SqlTrainingJob>()
            .SetSource(ToEntity(job))
            .Where(e => e.Id == id && (e.Status == a || e.Status == b || e.Status == c || e.Status == d))
            .ExecuteAffrowsAsync(cancellationToken);
        return affected > 0;
    }

    private static SqlTrainingJob ToEntity(TrainingJob job) => new()
    {
        Id = job.Id,
        ProjectId = job.ProjectId,
        Name = job.Name,
        Backend = job.Backend,
        ModelType = job.ModelType,
        DataRoot = job.DataRoot,
        OutputRoot = job.OutputRoot,
        Status = job.Status.ToString(),
        StartedAt = job.StartedAt,
        FinishedAt = job.FinishedAt,
        BestModelPath = job.BestModelPath,
        LastModelPath = job.LastModelPath,
        LogFile = job.LogFile,
        ProcessId = job.ProcessId,
        FailureMessage = job.FailureMessage,
        ConfigurationJson = JsonSerializer.Serialize(job.Configuration)
    };

    public async Task SaveAsync(TrainingJob job, CancellationToken cancellationToken = default)
    {
        if (job.ProjectId is Guid projectId)
        {
            await SqlProjectExecutionGuard.EnsureProjectAsync(_sqliteDb.Instance, projectId, cancellationToken).ConfigureAwait(false);
        }
        var existing = await GetByIdAsync(job.Id, cancellationToken).ConfigureAwait(false);
        if (existing?.ProjectId is Guid existingProjectId && existingProjectId != job.ProjectId)
        {
            await SqlProjectExecutionGuard.EnsureProjectAsync(_sqliteDb.Instance, existingProjectId, cancellationToken).ConfigureAwait(false);
        }

        var entity = new SqlTrainingJob
        {
            Id = job.Id,
            ProjectId = job.ProjectId,
            Name = job.Name,
            Backend = job.Backend,
            ModelType = job.ModelType,
            DataRoot = job.DataRoot,
            OutputRoot = job.OutputRoot,
            Status = job.Status.ToString(),
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            BestModelPath = job.BestModelPath,
            LastModelPath = job.LastModelPath,
            LogFile = job.LogFile,
            ProcessId = job.ProcessId,
            FailureMessage = job.FailureMessage,
            ConfigurationJson = JsonSerializer.Serialize(job.Configuration)
        };

        await _sqliteDb.Instance
            .InsertOrUpdate<SqlTrainingJob>()
            .SetSource(entity)
            .ExecuteAffrowsAsync(cancellationToken);

        _logger.LogDebug("Saved training job {JobId} {Name}", job.Id, job.Name);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (job?.ProjectId is Guid projectId)
        {
            await SqlProjectExecutionGuard.EnsureProjectAsync(_sqliteDb.Instance, projectId, cancellationToken).ConfigureAwait(false);
        }

        await _sqliteDb.Instance
            .Delete<SqlTrainingJob>()
            .Where(item => item.Id == id)
            .ExecuteAffrowsAsync(cancellationToken);

        _logger.LogInformation("Deleted training job {JobId}", id);
    }

    private TrainingJob MapFromEntity(SqlTrainingJob entity)
    {
        return TrainingJob.FromPersistence(
            entity.Id,
            entity.ProjectId,
            entity.Name,
            entity.Backend,
            entity.ModelType,
            entity.DataRoot,
            entity.OutputRoot,
            ParsePersistedStatus(entity.Id, entity.Status),
            entity.StartedAt,
            entity.FinishedAt,
            entity.BestModelPath,
            entity.LastModelPath,
            entity.LogFile,
            entity.ProcessId,
            JsonSerializer.Deserialize<TrainingConfiguration?>(entity.ConfigurationJson),
            entity.FailureMessage);
    }
}
