namespace DeepVisionSharp.Domain.ValueObjects;

/// <summary>
/// Annotation for one sample: an image-level class (classification) and/or box shapes
/// (detection). <see cref="IsReviewed"/> records that a detection sample was looked at,
/// so an empty box list means "no objects" rather than "not annotated yet".
/// </summary>
public sealed record AnnotationDocument(
    double ImageWidth,
    double ImageHeight,
    string? ImageClass = null,
    IReadOnlyList<AnnotationShape>? Shapes = null,
    bool IsReviewed = false)
{
    public IReadOnlyList<AnnotationShape> Boxes => Shapes ?? Array.Empty<AnnotationShape>();

    /// <summary>True when the sample carries a class, at least one box, or an explicit review mark.</summary>
    public bool HasAnnotations =>
        !string.IsNullOrWhiteSpace(ImageClass)
        || Boxes.Count > 0
        || IsReviewed;
}
