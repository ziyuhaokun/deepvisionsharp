namespace DeepVisionSharp.Domain.Entities;

public sealed class TrainedModel
{
    public TrainedModel(
        Guid id,
        Guid projectId,
        string name,
        string modelType,
        TrainedModelStatus status,
        string note,
        IReadOnlyList<string> classes,
        string checkpointPath,
        DateTimeOffset createdAt,
        int maxDetections)
    {
        Id = id;
        ProjectId = projectId;
        Name = name;
        ModelType = modelType;
        Status = status;
        Note = note;
        Classes = classes;
        CheckpointPath = checkpointPath;
        CreatedAt = createdAt;
        MaxDetections = maxDetections;
    }

    public Guid Id { get; }

    public Guid ProjectId { get; }

    public string Name { get; }

    public string ModelType { get; }

    public TrainedModelStatus Status { get; }

    public string Note { get; }

    public IReadOnlyList<string> Classes { get; }

    public string CheckpointPath { get; }

    public DateTimeOffset CreatedAt { get; }

    public int MaxDetections { get; }

    public static TrainedModel CreateNew(
        Guid projectId,
        string name,
        string modelType,
        TrainedModelStatus status,
        string note,
        IReadOnlyList<string> classes,
        string checkpointPath,
        int maxDetections = 500)
    {
        return new TrainedModel(
            Guid.NewGuid(),
            projectId,
            name.Trim(),
            modelType.Trim(),
            status,
            note.Trim(),
            classes,
            checkpointPath,
            DateTimeOffset.UtcNow,
            maxDetections);
    }

    public TrainedModel With(
        string? name = null,
        string? modelType = null,
        TrainedModelStatus? status = null,
        string? note = null,
        IReadOnlyList<string>? classes = null,
        string? checkpointPath = null,
        int? maxDetections = null)
    {
        return new TrainedModel(
            Id,
            ProjectId,
            name ?? Name,
            modelType ?? ModelType,
            status ?? Status,
            note ?? Note,
            classes ?? Classes,
            checkpointPath ?? CheckpointPath,
            CreatedAt,
            maxDetections ?? MaxDetections);
    }
}
