using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Domain.Repositories;

public interface IGalleryRepository
{
    Task<IReadOnlyList<ProjectGallery>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ProjectGallery?> GetByIdAsync(Guid galleryId, CancellationToken cancellationToken = default);

    Task<ProjectGallery?> GetSummaryByIdAsync(Guid galleryId, CancellationToken cancellationToken = default);

    Task<GallerySamplePage> ListSamplesAsync(Guid galleryId, int skip, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListSampleFileNamesAsync(
        Guid galleryId,
        GallerySampleFilter? filter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves one thumbnail image candidate per project (or none for empty projects).
    /// Uses the same gallery ranking as <see cref="ListByProjectAsync"/> and picks the
    /// first sample (by file name) of the highest-ranked gallery that has samples.
    /// Implementations should batch this into a single store round trip.
    /// </summary>
    Task<IReadOnlyList<ProjectThumbnailCandidate>> ListThumbnailCandidatesAsync(
        CancellationToken cancellationToken = default);

    Task<GallerySample?> GetSampleAsync(Guid galleryId, Guid sampleId, CancellationToken cancellationToken = default);

    Task<GallerySample?> GetSampleByFileNameAsync(Guid galleryId, string fileName, CancellationToken cancellationToken = default);

    Task AddSampleAsync(Guid galleryId, GallerySample sample, CancellationToken cancellationToken = default);

    Task UpdateSampleAsync(Guid galleryId, GallerySample sample, CancellationToken cancellationToken = default);

    Task<bool> RemoveSampleByFileNameAsync(Guid galleryId, string fileName, CancellationToken cancellationToken = default);

    Task<GallerySample?> GetAdjacentSampleAsync(Guid galleryId, Guid currentSampleId, int offset, CancellationToken cancellationToken = default);

    Task<int> GetSampleIndexAsync(Guid galleryId, Guid sampleId, CancellationToken cancellationToken = default);

    Task<ProjectGallery> CreateAsync(
        Guid projectId,
        string name,
        string description,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 同一项目下图库名称是否已被占用（大小写不敏感，与 OrdinalIgnoreCase 口径对齐）。
    /// <paramref name="excludeGalleryId"/> 用于编辑场景排除自身。
    /// </summary>
    Task<bool> ExistsByNameAsync(
        Guid projectId,
        string name,
        Guid? excludeGalleryId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates gallery name/description only. Does not load or replace samples.
    /// </summary>
    Task UpdateDetailsAsync(Guid galleryId, string name, string description, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists gallery metadata and reconciles the sample table to <see cref="ProjectGallery.Samples"/>.
    /// When <c>Samples</c> is empty, only metadata is written (existing samples are left unchanged)
    /// so summary-only callers cannot wipe the gallery. Callers that intentionally replace the
    /// full sample set must pass the complete list via <see cref="ProjectGallery.WithSamples"/>.
    /// </summary>
    Task SaveAsync(ProjectGallery gallery, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid galleryId, CancellationToken cancellationToken = default);

    Task<string?> GetAnchorFileNameAsync(Guid galleryId, CancellationToken cancellationToken = default);

    Task SetAnchorFileNameAsync(Guid galleryId, string? fileName, CancellationToken cancellationToken = default);
}
