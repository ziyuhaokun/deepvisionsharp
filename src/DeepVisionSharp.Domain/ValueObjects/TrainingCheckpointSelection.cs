namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>User-selected training checkpoint when saving to the model library.</summary>
public enum TrainingCheckpointSelection
{
    Best = 0,
    Last = 1
}

public enum TrainingCheckpointKind
{
    Best = 0,
    Last = 1
}
