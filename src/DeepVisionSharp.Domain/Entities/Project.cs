namespace DeepVisionSharp.Domain.Entities;

public sealed class Project
{
    public Project(
        Guid id,
        string name,
        DateTimeOffset createdAt,
        string description = "",
        string type = "Classification",
        int sampleCount = 0,
        int labelCount = 0,
        double progress = 0,
        DateTimeOffset? lastUpdatedAt = null,
        IReadOnlyList<string>? labels = null)
    {
        Id = id;
        Name = name;
        CreatedAt = createdAt;
        Description = description;
        Type = type;
        SampleCount = sampleCount;
        Labels = labels ?? Array.Empty<string>();
        LabelCount = Labels.Count > 0 ? Labels.Count : labelCount;
        Progress = progress;
        LastUpdatedAt = lastUpdatedAt ?? createdAt;
    }

    public Guid Id { get; }

    public string Name { get; }

    public DateTimeOffset CreatedAt { get; }

    public string Description { get; }

    public string Type { get; }

    public int SampleCount { get; }

    public int LabelCount { get; }

    public IReadOnlyList<string> Labels { get; }

    public double Progress { get; }

    public DateTimeOffset LastUpdatedAt { get; }

    public Project WithDetails(
        string name,
        string description,
        string type,
        int sampleCount,
        int labelCount,
        double progress,
        DateTimeOffset? lastUpdatedAt = null)
    {
        return new Project(
            Id,
            name,
            CreatedAt,
            description,
            type,
            sampleCount,
            labelCount,
            progress,
            lastUpdatedAt ?? DateTimeOffset.UtcNow,
            Labels);
    }
}
