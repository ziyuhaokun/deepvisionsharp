namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>A published set of classification previews from one training epoch.</summary>
public sealed record TrainingPreviewManifest(IReadOnlyList<TrainingPreviewSample> Samples)
{
    public const string FileName = "samples.json";
    public const string SampleDirectoryPrefix = "samples-";

    public static bool IsSampleDirectoryName(string? name) =>
        name is not null
        && name.StartsWith(SampleDirectoryPrefix, StringComparison.Ordinal)
        && Guid.TryParseExact(name.AsSpan(SampleDirectoryPrefix.Length), "N", out _);
}

public sealed record TrainingPreviewSample(
    string ImageFileName,
    string SampleName,
    string GroundTruth,
    string Prediction,
    float Confidence);
