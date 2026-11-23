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

        // Copy before opening: once the pool opens the file it may start a WAL, and a copy taken
        // afterwards can be a torn snapshot.
        BackupDatabaseBeforeMigration(dataDirectory);

        var dbPath = Path.Combine(dataDirectory, "deepvision.db");
        var connectionString = $"Data Source={dbPath};Pooling=true;Max Pool Size=5;BusyTimeout=5000";

        var db = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, connectionString)
            .UseAutoSyncStructure(false)
            .UseNoneCommandParameter(true)
            .Build();

        EnsureSchema(db, dataDirectory);
        ConfigureSqlitePragmas(db);
        // Only after the migration has committed: the backfill queries tables the migration creates,
        // and running it earlier both fails and holds a connection across the schema change.
        ScheduleSampleLabelIndexBackfill(db);

        _logger.LogInformation("Connected to SQLite database at {DatabasePath}", dbPath);

        return db;
    }

    private void EnsureSchema(IFreeSql db, string dataDirectory)
    {
        var versionFilePath = Path.Combine(dataDirectory, "schema.version");
        var fileVersion = ReadStoredSchemaVersion(versionFilePath);
        // The version inside the database is authoritative: the sidecar file can be lost or left
        // behind when the database file is copied on its own. A database that predates user_version
        // reports 0 and falls back to the sidecar.
        var userVersion = ReadUserVersion(db);
        var storedVersion = Math.Max(userVersion, fileVersion);
        if (storedVersion == SqliteSchemaVersions.Current && userVersion == fileVersion)
        {
            _logger.LogDebug("SQLite schema version {SchemaVersion} is up to date.", storedVersion);
            return;
        }

        if (storedVersion == SqliteSchemaVersions.Current)
        {
            // Only the two version locations disagree; nothing structural changes.
            WriteUserVersion(db, SqliteSchemaVersions.Current);
            WriteStoredSchemaVersion(versionFilePath, SqliteSchemaVersions.Current);
            _logger.LogInformation(
                "Aligned SQLite schema version locations to {SchemaVersion}.",
                SqliteSchemaVersions.Current);
            return;
        }

        _logger.LogInformation(
            "SQLite schema upgrade required: {StoredVersion} -> {CurrentVersion}",
            storedVersion,
            SqliteSchemaVersions.Current);

        // Existing databases may hold duplicate (GalleryId, FileName) rows that block unique indexes.
        // Also run when the version file is missing (storedVersion == 0) on a database that already
        // has tables: a lost schema.version must not let duplicates reach SyncStructure, which would
        // throw and poison the Lazy below for the rest of the process. Guard on the columns as well as
        // the table: a partial legacy schema is missing them, and the dedupe reads the mapped entity.
        if (storedVersion < 8
            && TableHasColumns(db, "Samples", "GalleryId", "FileName", "IsAnnotated", "Id")
            && TableHasColumns(db, "Annotations", "GalleryId", "FileName", "Id"))
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

        WriteUserVersion(db, SqliteSchemaVersions.Current);
        WriteStoredSchemaVersion(versionFilePath, SqliteSchemaVersions.Current);
    }

    /// <summary>
    /// Copies <c>deepvision.db</c> to <c>deepvision.db.pre-v{target}.bak</c> before a schema upgrade.
    /// Runs before the database is opened, so the copy cannot be torn by a WAL. A backup already
    /// present for the same target version is kept: it is the original pre-upgrade state, and
    /// overwriting it would destroy the only clean copy. A database already at the current version
    /// (either version location says so) is not copied.
    /// </summary>
    private void BackupDatabaseBeforeMigration(string dataDirectory)
    {
        var dbPath = Path.Combine(dataDirectory, "deepvision.db");
        if (!File.Exists(dbPath))
        {
            return;
        }

        var versionFilePath = Path.Combine(dataDirectory, "schema.version");
        var fileVersion = ReadStoredSchemaVersion(versionFilePath);
        var userVersion = ReadUserVersionFromFile(dbPath);
        if (fileVersion == SqliteSchemaVersions.Current || userVersion == SqliteSchemaVersions.Current)
        {
            return;
        }

        var backupPath = Path.Combine(
            dataDirectory, $"deepvision.db.pre-v{SqliteSchemaVersions.Current}.bak");
        if (File.Exists(backupPath))
        {
            _logger.LogWarning(
                "Schema backup {BackupPath} already exists; keeping it and migrating without a new copy.",
                backupPath);
            return;
        }

        File.Copy(dbPath, backupPath);
        _logger.LogInformation(
            "Backed up database to {BackupPath} before schema upgrade to {CurrentVersion}.",
            backupPath,
            SqliteSchemaVersions.Current);
    }

    /// <summary>Reads <c>PRAGMA user_version</c> without joining the shared connection pool.</summary>
    private static int ReadUserVersionFromFile(string dbPath)
    {
        try
        {
            using var connection = new System.Data.SQLite.SQLiteConnection(
                $"Data Source={dbPath};Pooling=false");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return Convert.ToInt32(command.ExecuteScalar());
        }
        catch
        {
            return 0;
        }
    }

    private static int ReadUserVersion(IFreeSql db)
    {
        try
        {
            return Convert.ToInt32(db.Ado.ExecuteScalar("PRAGMA user_version;"));
        }
        catch
        {
            return 0;
        }
    }

    private static void WriteUserVersion(IFreeSql db, int version)
    {
        db.Ado.ExecuteNonQuery($"PRAGMA user_version = {version};");
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

    /// <summary>True when <paramref name="table"/> exists and contains every named column.</summary>
    private static bool TableHasColumns(IFreeSql db, string table, params string[] columns)
    {
        try
        {
            var rows = db.Ado.ExecuteArray($"PRAGMA table_info(\"{table}\");");
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                // PRAGMA table_info columns: cid, name, type, notnull, dflt_value, pk.
                if (row.Length > 1 && row[1] is string name)
                {
                    present.Add(name);
                }
            }

            return columns.All(present.Contains);
        }
        catch
        {
            return false;
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
        return ExceptionChainContains(exception, "no such table");
    }

    /// <summary>True when a background task touched a database its owner already disposed.</summary>
    private static bool IsClosedDatabase(Exception exception)
    {
        return ExceptionChainContains(exception, "closed");
    }

    private static bool ExceptionChainContains(Exception exception, string fragment)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A closed database (the owner disposed SqliteDb, or the process is shutting down)
                // is expected here; only a failure against a live database is worth reporting.
                if (IsClosedDatabase(ex))
                {
                    return;
                }

                _logger.LogWarning(ex, "Failed to backfill sample label index.");
            }
        });
    }
}
