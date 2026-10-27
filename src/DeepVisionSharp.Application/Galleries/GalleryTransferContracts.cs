namespace DeepVisionSharp.Application.Galleries;

public interface IGalleryTransferService
{
    Task<GalleryTransferResult> ImportSamplesFromFilesAsync(
        Guid galleryId,
        IReadOnlyList<string> filePaths,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<GalleryTransferResult> ImportSamplesFromFolderAsync(
        Guid galleryId,
        string folderPath,
        bool recursive = false,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<GalleryTransferResult> ExportGalleryAsync(
        Guid galleryId,
        string outputFilePath,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<GalleryTransferResult> ImportGalleryAsync(
        Guid projectId,
        string inputFilePath,
        IProgress<GalleryTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    string GetGalleryDirectory(Guid projectId, Guid galleryId, bool ensureExists);
}

public interface ILocalFileDialogService
{
    Task<string[]?> SelectFilesAsync(string filter = "所有文件|*.*", bool allowMultiSelect = true);

    Task<string?> SaveFileAsync(string filter, string defaultFileName);

    Task<string?> SelectFolderAsync();
}

public sealed class GalleryTransferResult
{
    public GalleryTransferResult(bool succeeded, int total, int added, int failed, int skipped)
    {
        Succeeded = succeeded;
        Total = total;
        Added = added;
        Failed = failed;
        Skipped = skipped;
    }

    public bool Succeeded { get; }

    public int Total { get; }

    public int Added { get; }

    public int Failed { get; }

    public int Skipped { get; }

    public List<string> Errors { get; } = new();
}

public sealed class GalleryTransferProgress
{
    public GalleryTransferProgress(string stage, int processed, int total, int added, string? currentFile = null)
    {
        Stage = stage;
        Processed = processed;
        Total = total;
        Added = added;
        CurrentFile = currentFile;
    }

    public string Stage { get; }

    public int Processed { get; }

    public int Total { get; }

    public int Added { get; }

    public string? CurrentFile { get; }
}
