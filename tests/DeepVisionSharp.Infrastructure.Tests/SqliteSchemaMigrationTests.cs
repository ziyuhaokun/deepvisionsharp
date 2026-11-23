using System.Data.SQLite;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Modules;
using DeepVisionSharp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

/// <summary>
/// 覆盖 schema 迁移前的备份：库文件在升级前被完整复制，版本号同时写入 schema.version 与 PRAGMA user_version。
/// </summary>
[Collection(SqliteSerialCollection.Name)]
public sealed class SqliteSchemaMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        FindRepositoryTemp(), "schema-" + Guid.NewGuid().ToString("N"));

    public SqliteSchemaMigrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            SQLiteConnection.ClearAllPools();
        }
        catch
        {
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    Directory.Delete(_directory, recursive: true);
                }

                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void Fresh_database_writes_both_version_locations_and_creates_no_backup()
    {
        var data = DataDir(0);
        Open(data);

        Assert.Equal(SqliteSchemaVersions.Current, ReadFileVersion(data));
        Assert.Equal(SqliteSchemaVersions.Current, ReadUserVersion(data));
        Assert.Empty(Directory.GetFiles(data, "deepvision.db.pre-v*.bak"));
    }

    [Fact]
    public void Upgrade_from_file_version_copies_the_database_before_migrating()
    {
        var data = DataDir(1);
        SeedLegacyDatabase(data, userVersion: 0);
        File.WriteAllText(Path.Combine(data, "schema.version"), "9");
        var before = File.ReadAllBytes(Path.Combine(data, "deepvision.db"));

        Open(data);

        var backup = Assert.Single(Directory.GetFiles(data, "deepvision.db.pre-v*.bak"));
        Assert.Equal("deepvision.db.pre-v10.bak", Path.GetFileName(backup));
        Assert.Equal(before, File.ReadAllBytes(backup));
        Assert.Equal(SqliteSchemaVersions.Current, ReadFileVersion(data));
        Assert.Equal(SqliteSchemaVersions.Current, ReadUserVersion(data));
    }

    [Fact]
    public void Missing_version_file_with_existing_tables_still_backs_up_and_migrates()
    {
        var data = DataDir(2);
        SeedLegacyDatabase(data, userVersion: 0);
        var before = File.ReadAllBytes(Path.Combine(data, "deepvision.db"));

        Open(data);

        var backup = Assert.Single(Directory.GetFiles(data, "deepvision.db.pre-v*.bak"));
        Assert.Equal(before, File.ReadAllBytes(backup));
        Assert.Equal(SqliteSchemaVersions.Current, ReadFileVersion(data));
        Assert.Equal(SqliteSchemaVersions.Current, ReadUserVersion(data));
    }

    [Fact]
    public void User_version_is_the_source_when_the_version_file_is_stale()
    {
        var data = DataDir(3);
        SeedLegacyDatabase(data, userVersion: SqliteSchemaVersions.Current);
        File.WriteAllText(Path.Combine(data, "schema.version"), "3");

        Open(data);

        Assert.Empty(Directory.GetFiles(data, "deepvision.db.pre-v*.bak"));
        Assert.Equal(SqliteSchemaVersions.Current, ReadFileVersion(data));
        Assert.Equal(SqliteSchemaVersions.Current, ReadUserVersion(data));
    }

    [Fact]
    public void Current_database_opens_without_a_backup()
    {
        var data = DataDir(4);
        Open(data);
        var modified = File.GetLastWriteTimeUtc(Path.Combine(data, "deepvision.db"));

        Open(data);

        Assert.Empty(Directory.GetFiles(data, "deepvision.db.pre-v*.bak"));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(Path.Combine(data, "deepvision.db")));
    }

    private string DataDir(int index) => Path.Combine(_directory, "data" + index);

    private static void Open(string dataDirectory)
    {
        using var services = new ServiceCollection()
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddSingleton<IDataRootProvider>(new DefaultDataRootProvider(dataDirectory))
            .AddDataModule()
            .BuildServiceProvider();
        using var db = services.GetRequiredService<SqliteDb>().Instance;
        Assert.NotNull(db.Ado.ExecuteScalar("SELECT COUNT(*) FROM sqlite_master WHERE type='table';"));
        // The label-index backfill runs on a background task and keeps a pooled connection. Wait it
        // out so the assertions below can reopen the file, and so disposal does not race it.
        Thread.Sleep(300);
    }

    private static void SeedLegacyDatabase(string dataDirectory, int userVersion)
    {
        Directory.CreateDirectory(dataDirectory);
        using var connection = new SQLiteConnection($"Data Source={Path.Combine(dataDirectory, "deepvision.db")}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE Samples (Id TEXT PRIMARY KEY); PRAGMA user_version = {userVersion};";
        command.ExecuteNonQuery();
    }

    private static int ReadFileVersion(string dataDirectory)
    {
        var text = File.ReadAllText(Path.Combine(dataDirectory, "schema.version")).Trim();
        return int.Parse(text);
    }

    private static int ReadUserVersion(string dataDirectory)
    {
        using var connection = new SQLiteConnection($"Data Source={Path.Combine(dataDirectory, "deepvision.db")};Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string FindRepositoryTemp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "DeepVisionSharp.slnx")))
        {
            directory = directory.Parent ?? throw new DirectoryNotFoundException("DeepVisionSharp.slnx");
        }

        return Path.Combine(directory.FullName, "temp");
    }
}
