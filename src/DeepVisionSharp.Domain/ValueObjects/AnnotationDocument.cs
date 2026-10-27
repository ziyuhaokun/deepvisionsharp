namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>Image-level classification annotation for one sample.</summary>
public sealed record AnnotationDocument(
    double ImageWidth,
    double ImageHeight,
    string? ImageClass = null);
