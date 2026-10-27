using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Persistence;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class SampleFileMover : ISampleFileMover
{
    private readonly IProjectFileStore _fileStore;
    private readonly ILogger<SampleFileMover> _logger;
    private readonly SqliteDb _sqliteDb;

    public SampleFileMover(IProjectFileStore fileStore, SqliteDb sqliteDb, ILogger<SampleFileMover> logger)
    {
        _fileStore = fileStore;
        _sqliteDb = sqliteDb;
        _logger = logger;
    }

    public async Task<SampleFileMoveResult> MoveAsync(
        Guid projectId,
        Guid sourceGalleryId,
        Guid targetGalleryId,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        SqlProjectExecutionGuard.EnsureProject(_sqliteDb.Instance, projectId);
        var sourceGallery = _sqliteDb.Instance.Select<SqlGallery>().Where(item => item.Id == sourceGalleryId).First();
        var targetGallery = _sqliteDb.Instance.Select<SqlGallery>().Where(item => item.Id == targetGalleryId).First();
        if (sourceGallery?.ProjectId != projectId || targetGallery?.ProjectId != projectId)
        {
            return new SampleFileMoveResult(SampleFileMoveOutcome.Failed, "图库不属于当前项目。");
        }
        var safeFileName = Path.GetFileName(fileName);
        var sourceImagePath = _fileStore.ResolveSampleImagePath(projectId, sourceGalleryId, safeFileName);
        if (string.IsNullOrWhiteSpace(sourceImagePath) || !File.Exists(sourceImagePath))
        {
            return new SampleFileMoveResult(SampleFileMoveOutcome.SourceImageMissing);
        }

        var targetImageDir = _fileStore.GetGalleryImagesDirectory(projectId, targetGalleryId, ensureExists: true);
        var targetImagePath = Path.Combine(targetImageDir, safeFileName);
        if (File.Exists(targetImagePath))
        {
            return new SampleFileMoveResult(SampleFileMoveOutcome.TargetImageExists);
        }

        var imageMoved = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await SampleAnnotationFiles.PreserveLegacyCollisionAsync(_sqliteDb.Instance, sourceGalleryId, sourceImagePath, cancellationToken);
            var sourceAnnotationPath = File.Exists(SampleAnnotationFiles.GetPath(sourceImagePath))
                ? SampleAnnotationFiles.GetPath(sourceImagePath)
                : await SampleAnnotationFiles.FindOwnedLegacyAsync(sourceImagePath, cancellationToken);
            File.Move(sourceImagePath, targetImagePath);
            imageMoved = true;

            var targetAnnotationPath = SampleAnnotationFiles.GetPath(targetImagePath);
            if (sourceAnnotationPath is not null)
            {
                File.Move(sourceAnnotationPath, targetAnnotationPath);
            }

            return new SampleFileMoveResult(SampleFileMoveOutcome.Moved);
        }
        catch (Exception ex)
        {
            if (imageMoved)
            {
                try { File.Move(targetImagePath, sourceImagePath); }
                catch (Exception rollback) { _logger.LogError(rollback, "Failed to roll back sample {FileName}", safeFileName); }
            }
            _logger.LogWarning(
                ex,
                "Failed to move sample file {FileName} from gallery {SourceGalleryId} to {TargetGalleryId}",
                safeFileName,
                sourceGalleryId,
                targetGalleryId);

            return new SampleFileMoveResult(SampleFileMoveOutcome.Failed, ex.Message);
        }
    }
}
