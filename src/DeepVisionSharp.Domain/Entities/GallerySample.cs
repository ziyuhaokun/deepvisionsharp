namespace DeepVisionSharp.Domain.Entities;

public sealed class GallerySample
{
    public GallerySample(
        Guid id,
        Guid galleryId,
        string fileName,
        bool isAnnotated = false,
        bool hasError = false,
        string? errorMessage = null)
    {
        Id = id;
        GalleryId = galleryId;
        FileName = fileName;
        IsAnnotated = isAnnotated;
        HasError = hasError;
        ErrorMessage = errorMessage;
    }

    public Guid Id { get; }

    public Guid GalleryId { get; }

    public string FileName { get; }

    public bool IsAnnotated { get; }

    public bool HasError { get; }

    public string? ErrorMessage { get; }
}
