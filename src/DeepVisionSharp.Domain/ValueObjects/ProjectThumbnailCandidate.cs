namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Resolves the single image candidate used as a project's dashboard thumbnail.
/// Produced by <see cref="DeepVisionSharp.Domain.Repositories.IGalleryRepository.ListThumbnailCandidatesAsync"/>
/// with the same gallery/sample ranking used by interactive gallery listing.
/// </summary>
public sealed record ProjectThumbnailCandidate(Guid ProjectId, Guid GalleryId, string FileName);