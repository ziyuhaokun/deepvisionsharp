namespace DeepVisionSharp.Application.Ports;

public interface IProjectFileStore
{
    string DataDirectory { get; }

    string GetProjectDirectory(Guid projectId, bool ensureExists = false);

    string GetGalleryDirectory(Guid projectId, Guid galleryId, bool ensureExists = false);

    string GetGalleryImagesDirectory(Guid projectId, Guid galleryId, bool ensureExists = false);

    string? ResolveSampleImagePath(Guid projectId, Guid galleryId, string fileName);

    string GetTrainingRunsRoot(Guid projectId, string? workspace = null);

    string GetTrainingRunPath(Guid projectId, string version);

    string GetTrainingLogPath(Guid projectId, string version);

    string GetModelsDirectory(Guid projectId, bool ensureExists = false);
}
