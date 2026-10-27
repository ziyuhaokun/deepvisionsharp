using FreeSql;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Persistence;

internal sealed class SqliteDb
{
    private readonly ILogger<SqliteDb> _logger;
    private readonly IDataRootProvider _dataRoot;
    private readonly object _resetSync = new();
    private Lazy<IFreeSql> _instance;

    public SqliteDb(IDataRootProvider dataRoot, ILogger<SqliteDb> logger)
    {
        _dataRoot = dataRoot;
        _logger = logger;
        _instance = CreateLazy();
    }

    public IFreeSql Instance
    {
        get
        {
            try
            {
                return _instance.Value;
            }
            catch
            {
                // ExecutionAndPublication caches the exception as well as the value, so a failed open
                // (locked file, transient IO error) would make every later access throw the same
                // stale exception and leave the app unusable. Reset once and retry.
                lock (_resetSync)
                {
                    if (!_instance.IsValueCreated)
                    {
                        _instance = CreateLazy();
                    }
                }

                return _instance.Value;
            }
        }
    }

    private Lazy<IFreeSql> CreateLazy() =>
        new(CreateDb, LazyThreadSafetyMode.ExecutionAndPublication);

    private IFreeSql CreateDb()
    {
        var dataDirectory = _dataRoot.DataDirectory;
        Directory.CreateDirectory(dataDirectory);

        var dbPath = Path.Combine(dataDirectory, "deepvision.db");
        var connectionString = $"Data Source={dbPath};Pooling=true;Max Pool Size=5";

        var db = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, connectionString)
            .UseAutoSyncStructure(false)
            .UseNoneCommandParameter(true)
            .Build();

        EnsureSchema(db, dataDirectory);
        ConfigureSqlitePragmas(db);
        ScheduleSampleLabelIndexBackfill(db);

        _logger.LogInformation("Connected WPF shell to SQLite database at {DatabasePath}", dbPath);

        return db;
    }

    private void EnsureSchema(IFreeSql db, string dataDirectory)
    {
        var versionFilePath = Path.Combine(dataDirectory, "schema.version");
        var storedVersion = ReadStoredSchemaVersion(versionFilePath);
        if (storedVersion == SqliteSchemaVersions.Current)
        {
            _logger.LogDebug("SQLite schema version {SchemaVersion} is up to date.", storedVersion);
            return;
        }

        _logger.LogInformation(
            "SQLite schema upgrade required: {StoredVersion} -> {CurrentVersion}",
            storedVersion,
            SqliteSchemaVersions.Current);

        // Existing databases may hold duplicate (GalleryId, FileName) rows that block unique indexes.
        // Also run when the version file is missing (storedVersion == 0) on a database that already
        // has tables: a lost schema.version must not let duplicates reach SyncStructure, which would
        // throw and poison the Lazy below for the rest of the process.
        if (storedVersion < 8 && HasExistingTables(db))
        {
            DedupeSamplesAndAnnotationsByGalleryFileName(db);
            DropLegacyGalleryFileNameIndexes(db);
        }

        db.CodeFirst.SyncStructure(
            typeof(SqlProject),
            typeof(SqlProjectLabel),
            typeof(SqlGallery),
            typeof(SqlSample),
            typeof(SqlModel),
            typeof(SqlTrainingJob),
            typeof(SqlAnnotation),
            typeof(SqlSampleLabelIndex));

        if (storedVersion < 5)
        {
            var migratedProjects = db.Ado.ExecuteNonQuery(
                "UPDATE \"Projects\" " +
                "SET \"Type\" = 'SemanticSegmentation' " +
                "WHERE \"Type\" = 'Segmentation' COLLATE NOCASE;");
            _logger.LogInformation(
                "Migrated {ProjectCount} legacy Segmentation projects to SemanticSegmentation.",
                migratedProjects);
        }

        if (storedVersion < 8)
        {
            // Ensure non-unique predecessors are gone even if SyncStructure left them.
            DropLegacyGalleryFileNameIndexes(db);
        }

        if (storedVersion < 10)
        {
            DropLegacyGallerySystemKindColumn(db);
        }

        WriteStoredSchemaVersion(versionFilePath, SqliteSchemaVersions.Current);
    }

    /// <summary>
    /// Removes duplicate Samples / Annotations rows that share the same gallery + file name
    /// so v8 unique indexes can be created. Prefer annotated samples; prefer highest annotation Id.
    /// </summary>
    private void DedupeSamplesAndAnnotationsByGalleryFileName(IFreeSql db)
    {
        try
        {
            using var connectionLease = db.Ado.MasterPool.Get();
            var connection = connectionLease.Value;
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            using var transaction = connection.BeginTransaction();
            try
            {
                var samples = db.Select<SqlSample>().WithTransaction(transaction).ToList();
                var sampleIdsToDelete = samples
                    .GroupBy(
                        sample => (sample.GalleryId, FileName: sample.FileName),
                        new GalleryFileNameKeyComparer())
                    .Where(group => group.Count() > 1)
                    .SelectMany(group =>
                    {
                        var keep = group
                            .OrderByDescending(sample => sample.IsAnnotated)
                            .ThenBy(sample => sample.Id)
                            .First();
                        return group.Where(sample => sample.Id != keep.Id).Select(sample => sample.Id);
                    })
                    .ToArray();

                if (sampleIdsToDelete.Length > 0)
                {
                    db.Delete<SqlSample>().WithTransaction(transaction).Where(sample => sampleIdsToDelete.Contains(sample.Id)).ExecuteAffrows();

                    _logger.LogWarning(
                        "Removed {DuplicateCount} duplicate Samples rows before unique index upgrade.",
                        sampleIdsToDelete.Length);
                }

                var annotations = db.Select<SqlAnnotation>().WithTransaction(transaction).ToList();
                var annotationIdsToDelete = annotations
                    .GroupBy(
                        annotation => (annotation.GalleryId, FileName: annotation.FileName),
                        new GalleryFileNameKeyComparer())
                    .Where(group => group.Count() > 1)
                    .SelectMany(group =>
                    {
                        var keep = group.OrderByDescending(annotation => annotation.Id).First();
                        return group.Where(annotation => annotation.Id != keep.Id).Select(annotation => annotation.Id);
                    })
                    .ToArray();

                if (annotationIdsToDelete.Length > 0)
                {
                    db.Delete<SqlAnnotation>().WithTransaction(transaction).Where(annotation => annotationIdsToDelete.Contains(annotation.Id)).ExecuteAffrows();

                    _logger.LogWarning(
                        "Removed {DuplicateCount} duplicate Annotations rows before unique index upgrade.",
                        annotationIdsToDelete.Length);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            // Tables may be incomplete mid-upgrade; SyncStructure will recreate and unique apply on
            // empty/clean data. Anything else (locked database, IO error) must not be swallowed:
            // SyncStructure would then fail on the duplicates and the Lazy below would cache that
            // exception for the rest of the process, leaving the app unusable with no way to retry.
            _logger.LogWarning(ex, "Failed to dedupe Samples/Annotations before unique index upgrade.");
            if (!IsMissingTableError(ex))
            {
                throw;
            }
        }
    }

    /// <summary>
    /// True when the SQLite file already contains application tables, regardless of the version file.
    /// </summary>
    private static bool HasExistingTables(IFreeSql db)
    {
        try
        {
            return db.Ado.ExecuteScalar(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Samples';") is long count
                && count > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// SQLite reports a missing table as "no such table"; any other failure is a real problem.
    /// </summary>
    private static bool IsMissingTableError(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void DropLegacyGalleryFileNameIndexes(IFreeSql db)
    {
        try
        {
            db.Ado.ExecuteNonQuery("DROP INDEX IF EXISTS \"IX_Samples_GalleryId_FileName\";");
            db.Ado.ExecuteNonQuery("DROP INDEX IF EXISTS \"IX_Annotations_GalleryId_FileName\";");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to drop legacy non-unique gallery file-name indexes (may not exist).");
        }
    }

    /// <summary>Removes the archived unsupervised-anomaly column from databases created before v10.</summary>
    private void DropLegacyGallerySystemKindColumn(IFreeSql db)
    {
        try
        {
            using var connectionLease = db.Ado.MasterPool.Get();
            var connection = connectionLease.Value;
            if (connection.State != System.Data.ConnectionState.Open)
            {
                connection.Open();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(\"Galleries\");";
                using var reader = command.ExecuteReader();
                var hasColumn = false;
                while (reader.Read())
                {
                    if (string.Equals(reader["name"]?.ToString(), "SystemKind", StringComparison.OrdinalIgnoreCase))
                    {
                        hasColumn = true;
                        break;
                    }
                }

                if (!hasColumn)
                {
                    return;
                }
            }

            db.Ado.ExecuteNonQuery("ALTER TABLE \"Galleries\" DROP COLUMN \"SystemKind\";");
            _logger.LogInformation("Dropped archived Galleries.SystemKind column.");
        }
        catch (Exception ex)
        {
            // The column is no longer mapped; leaving it in place is harmless on old SQLite builds.
            _logger.LogWarning(ex, "Failed to drop archived Galleries.SystemKind column.");
        }
    }

    private sealed class GalleryFileNameKeyComparer : IEqualityComparer<(Guid GalleryId, string FileName)>
    {
        public bool Equals((Guid GalleryId, string FileName) x, (Guid GalleryId, string FileName) y)
            => x.GalleryId == y.GalleryId
               && string.Equals(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid GalleryId, string FileName) obj)
            => HashCode.Combine(obj.GalleryId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FileName ?? string.Empty));
    }

    private static int ReadStoredSchemaVersion(string versionFilePath)
    {
        if (!File.Exists(versionFilePath))
        {
            return 0;
        }

        var text = File.ReadAllText(versionFilePath).Trim();
        return int.TryParse(text, out var version) ? version : 0;
    }

    private static void WriteStoredSchemaVersion(string versionFilePath, int version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(versionFilePath)!);
        File.WriteAllText(versionFilePath, version.ToString());
    }

    private static void ConfigureSqlitePragmas(IFreeSql db)
    {
        db.Ado.ExecuteNonQuery("PRAGMA journal_mode=WAL;");
        db.Ado.ExecuteNonQuery("PRAGMA synchronous=NORMAL;");
        db.Ado.ExecuteNonQuery("PRAGMA busy_timeout=5000;");
        db.Ado.ExecuteNonQuery("PRAGMA foreign_keys=ON;");
    }

    private void ScheduleSampleLabelIndexBackfill(IFreeSql db)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await GallerySampleLabelIndexMaintainer.BackfillMissingAsync(db).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to backfill sample label index.");
            }
        });
    }
}
