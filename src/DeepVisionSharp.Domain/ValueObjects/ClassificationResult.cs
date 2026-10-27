namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// One image-level classification result shared by the compute adapters and the
/// application layer. Kept in Domain so engine assemblies do not depend on Application.
/// </summary>
public sealed class ClassificationResult
{
    public string Label { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public double Confidence { get; set; }
}
