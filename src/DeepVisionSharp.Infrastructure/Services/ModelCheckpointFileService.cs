using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ModelCheckpointFileService : IModelCheckpointFileService
{
    private readonly ILogger<ModelCheckpointFileService> _logger;
    private readonly SqliteDb _sqliteDb;
    private readonly IProjectFileStore _fileStore;

    public ModelCheckpointFileService(SqliteDb sqliteDb, IProjectFileStore fileStore, ILogger<ModelCheckpointFileService> logger)
    {
        _logger = logger;
        _sqliteDb = sqliteDb;
        _fileStore = fileStore;
    }

    public bool Exists(string? checkpointPath) =>
        !string.IsNullOrWhiteSpace(checkpointPath) && File.Exists(checkpointPath);

    public bool TryGetDirectory(string checkpointPath, out string directory)
    {
        directory = Path.GetDirectoryName(checkpointPath) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory);
    }

    /// <summary>只拷贝 <c>.pt</c> 与可选 <c>.pt.info</c>。</summary>
    public string? Export(string checkpointPath, string exportRoot, string folderName)
    {
        if (!Exists(checkpointPath))
        {
            return null;
        }

        try
        {
            var exportDirectory = Path.Combine(exportRoot, SanitizeFolderName(folderName));
            Directory.CreateDirectory(exportDirectory);

            var destinationCheckpoint = Path.Combine(exportDirectory, Path.GetFileName(checkpointPath));
            File.Copy(checkpointPath, destinationCheckpoint, overwrite: true);

            var infoPath = checkpointPath + ".info";
            if (File.Exists(infoPath))
            {
                File.Copy(infoPath, Path.Combine(exportDirectory, Path.GetFileName(infoPath)), overwrite: true);
            }

            return exportDirectory;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to export model checkpoint from {CheckpointPath}", checkpointPath);
            return null;
        }
    }

    public void DeleteFiles(string checkpointPath)
    {
        SqlProjectExecutionGuard.EnsurePathWritable(_sqliteDb.Instance, _fileStore, checkpointPath);
        if (string.IsNullOrWhiteSpace(checkpointPath))
        {
            return;
        }

        try
        {
            if (File.Exists(checkpointPath))
            {
                File.Delete(checkpointPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete model file {ModelPath}", checkpointPath);
        }

        try
        {
            var infoPath = checkpointPath + ".info";
            if (File.Exists(infoPath))
            {
                File.Delete(infoPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete model metadata file for {ModelPath}", checkpointPath);
        }
    }

    private static string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "model";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "model" : sanitized;
    }
}
