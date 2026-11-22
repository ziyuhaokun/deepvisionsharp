using System.Data.Common;
using FreeSql;
using FreeSql.DataAnnotations;

using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services.Transfer;

/// <summary>
/// The on-disk layout of a <c>.project</c> package. Export writes it, import reads it, and the
/// two must agree, so the table list lives here once.
/// </summary>
/// <remarks>
/// A package carries everything that belongs to one project: its galleries, samples, annotations
/// (classification flags and detection boxes), models and training jobs. Model weight files and
/// their <c>.info</c> sidecars travel as rows of <see cref="SqlPackageFile"/> because a checkpoint
/// path points at a single file, not a directory tree. Training run directories (datasets, logs,
/// intermediate checkpoints) are regenerable and deliberately not included.
/// </remarks>
internal static class ProjectPackage
{
    /// <summary>Rows that describe the project. Read and written as whole tables.</summary>
    public static readonly Type[] Tables =
    [
        typeof(SqlProject),
        typeof(SqlProjectLabel),
        typeof(SqlGallery),
        typeof(SqlSample),
        typeof(SqlAnnotation),
        typeof(SqlModel),
        typeof(SqlTrainingJob),
        typeof(SqlPackageFile)
    ];

    public static void SyncStructure(IFreeSql db) => db.CodeFirst.SyncStructure(Tables);

    public static IFreeSql Create(string filePath)
    {
        var db = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, ConnectionString(filePath, readOnly: false))
            .UseAutoSyncStructure(true)
            .UseNoneCommandParameter(true)
            .Build();

        try
        {
            SyncStructure(db);
            return db;
        }
        catch
        {
            db.Dispose();
            throw;
        }
    }

    public static IFreeSql Open(string filePath, bool readOnly = true)
    {
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, ConnectionString(filePath, readOnly))
            .UseAutoSyncStructure(false)
            .UseNoneCommandParameter(true)
            .Build();
    }

    /// <summary>
    /// 反斜杠在连接串里会被 SQLite 当成转义符，<c>\t</c> 这类序列会让库文件打不开。
    /// 统一换成正斜杠，Windows 同样接受。
    /// </summary>
    private static string ConnectionString(string filePath, bool readOnly) => new DbConnectionStringBuilder
    {
        ["Data Source"] = Path.GetFullPath(filePath).Replace('\\', '/'),
        ["Read Only"] = readOnly,
        ["Pooling"] = false
    }.ConnectionString;
}

/// <summary>
/// One file shipped inside a package, keyed by its path relative to the project directory.
/// </summary>
[Table(Name = "PackageFiles")]
internal sealed class SqlPackageFile
{
    [Column(IsPrimary = true)]
    public string RelativePath { get; set; } = string.Empty;

    public byte[] Content { get; set; } = [];
}
