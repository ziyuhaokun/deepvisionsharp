using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class ProjectFileStore : IProjectFileStore
{
    private readonly string _legacyGalleryRoot;
    private readonly ILogger<ProjectFileStore> _logger;

    public ProjectFileStore(IDataRootProvider dataRoot, ILogger<ProjectFileStore> logger)
    {
        _logger = logger;
        DataDirectory = dataRoot.DataDirectory;

        _legacyGalleryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Vision.DeepVisionSharp",
            "galleries");

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(ProjectsRoot);
    }

    public string DataDirectory { get; }

    private string ProjectsRoot => Path.Combine(DataDirectory, "projects");

    public string GetProjectDirectory(Guid projectId, bool ensureExists = false)
    {
        var directory = Path.Combine(ProjectsRoot, projectId.ToString());
        if (ensureExists)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    public string GetGalleryDirectory(Guid projectId, Guid galleryId, bool ensureExists = false)
    {
        var directory = Path.Combine(GetProjectDirectory(projectId, ensureExists), "galleries", galleryId.ToString());
        if (ensureExists)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    public string GetGalleryImagesDirectory(Guid projectId, Guid galleryId, bool ensureExists = false)
    {
        var directory = Path.Combine(GetGalleryDirectory(projectId, galleryId, ensureExists), "images");
        if (ensureExists)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    public string? ResolveSampleImagePath(Guid projectId, Guid galleryId, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        var candidates = new[]
        {
            Path.Combine(GetGalleryImagesDirectory(projectId, galleryId), safeFileName),
            Path.Combine(_legacyGalleryRoot, projectId.ToString("N"), galleryId.ToString("N"), "images", safeFileName),
            Path.Combine(_legacyGalleryRoot, projectId.ToString("N"), galleryId.ToString("N"), safeFileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        _logger.LogDebug(
            "Sample image not found for project {ProjectId}, gallery {GalleryId}, file {FileName}",
            projectId,
            galleryId,
            safeFileName);

        return null;
    }

    public string GetTrainingRunsRoot(Guid projectId, string? workspace = null)
    {
        workspace ??= "default";
        return Path.Combine(GetProjectDirectory(projectId), "runs", workspace);
    }

    public string GetTrainingRunPath(Guid projectId, string version)
    {
        return Path.Combine(GetTrainingRunsRoot(projectId), version);
    }

    public string GetTrainingLogPath(Guid projectId, string version)
    {
        return Path.Combine(GetTrainingRunPath(projectId, version), "train.log");
    }

    public string GetModelsDirectory(Guid projectId, bool ensureExists = false)
    {
        var directory = Path.Combine(GetProjectDirectory(projectId, ensureExists), "models");
        if (ensureExists)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }
}
