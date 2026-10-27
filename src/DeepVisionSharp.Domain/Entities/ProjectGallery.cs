namespace DeepVisionSharp.Domain.Entities;

public sealed class ProjectGallery
{
    public ProjectGallery(
        Guid id,
        Guid projectId,
        string name,
        string description,
        int sampleCount,
        int annotatedCount,
        DateTimeOffset lastUpdatedAt,
        IReadOnlyList<GallerySample>? samples = null)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        Description = description;
        Samples = samples ?? Array.Empty<GallerySample>();
        SampleCount = Samples.Count > 0 ? Samples.Count : sampleCount;
        AnnotatedCount = Samples.Count > 0 ? Samples.Count(sample => sample.IsAnnotated) : annotatedCount;
        LastUpdatedAt = lastUpdatedAt;
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    public string Name { get; }

    public string Description { get; }

    public int SampleCount { get; }

    public int AnnotatedCount { get; }

    public DateTimeOffset LastUpdatedAt { get; }

    public IReadOnlyList<GallerySample> Samples { get; }

    public double Progress => SampleCount > 0 ? (double)AnnotatedCount / SampleCount : 0;

    public ProjectGallery WithSamples(IReadOnlyList<GallerySample> samples, DateTimeOffset? lastUpdatedAt = null)
    {
        return new ProjectGallery(
            Id,
            ProjectId,
            Name,
            Description,
            samples.Count,
            samples.Count(sample => sample.IsAnnotated),
            lastUpdatedAt ?? DateTimeOffset.UtcNow,
            samples);
    }

    public ProjectGallery WithDetails(string name, string description, DateTimeOffset? lastUpdatedAt = null)
    {
        return new ProjectGallery(
            Id,
            ProjectId,
            name,
            description,
            SampleCount,
            AnnotatedCount,
            lastUpdatedAt ?? DateTimeOffset.UtcNow,
            Samples);
    }
}
