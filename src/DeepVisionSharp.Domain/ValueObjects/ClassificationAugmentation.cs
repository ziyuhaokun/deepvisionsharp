namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Classification training augmentations the TorchSharp backend actually applies.
/// Defaults are off for industrial fixed-camera OK/NG workflows.
/// </summary>
public sealed record ClassificationAugmentation(
    double FlipLeftRight = 0.0,
    double FlipUpDown = 0.0)
{
    public static ClassificationAugmentation Disabled { get; } = new();

    public static ClassificationAugmentation Normalize(ClassificationAugmentation? value)
    {
        var source = value ?? Disabled;
        return new ClassificationAugmentation(
            FlipLeftRight: Clamp01(source.FlipLeftRight),
            FlipUpDown: Clamp01(source.FlipUpDown));
    }

    private static double Clamp01(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }
}
