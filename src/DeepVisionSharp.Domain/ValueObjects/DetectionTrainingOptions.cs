namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Detection-only training parameters. Kept separate from classification augmentation so a
/// classification job never grows fields that its backend ignores.
/// </summary>
public sealed record DetectionTrainingOptions(
    double NmsIouThreshold = 0.45,
    double EvaluationConfidence = 0.25,
    int MaxDetections = 100,
    bool HorizontalFlip = true)
{
    public static DetectionTrainingOptions Default { get; } = new();

    public static DetectionTrainingOptions Normalize(DetectionTrainingOptions? value)
    {
        value ??= Default;
        return value with
        {
            NmsIouThreshold = Math.Clamp(value.NmsIouThreshold, 0.05, 0.95),
            EvaluationConfidence = Math.Clamp(value.EvaluationConfidence, 0.01, 0.99),
            MaxDetections = Math.Clamp(value.MaxDetections, 1, 1000)
        };
    }
}
