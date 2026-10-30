namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>One detected box in original-image pixels, shared by the engine and the application layer.</summary>
public sealed class DetectionBoxResult
{
    public string Label { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public double Confidence { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public AnnotationShape ToShape() => new(
        string.IsNullOrWhiteSpace(Label) ? ClassName : Label,
        AnnotationShapeKind.Rectangle,
        X,
        Y,
        Width,
        Height);
}
