namespace DeepVisionSharp.Domain.ValueObjects;

public enum AnnotationShapeKind
{
    /// <summary>Axis-aligned box stored as two corner points (Labelme <c>rectangle</c>).</summary>
    Rectangle
}

/// <summary>
/// One object annotation in original-image pixel coordinates, top-left origin.
/// Detection samples can hold any number of shapes; classification samples hold none.
/// </summary>
public sealed record AnnotationShape(
    string Label,
    AnnotationShapeKind Kind,
    double X,
    double Y,
    double Width,
    double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool HasArea =>
        !string.IsNullOrWhiteSpace(Label)
        && Width > 0
        && Height > 0;

    /// <summary>Clips the box to the image bounds; returns null when nothing visible remains.</summary>
    public AnnotationShape? ClampTo(double imageWidth, double imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return HasArea ? this : null;
        }

        var left = Math.Clamp(Math.Min(X, Right), 0, imageWidth);
        var top = Math.Clamp(Math.Min(Y, Bottom), 0, imageHeight);
        var right = Math.Clamp(Math.Max(X, Right), 0, imageWidth);
        var bottom = Math.Clamp(Math.Max(Y, Bottom), 0, imageHeight);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(Label))
        {
            return null;
        }

        return this with { X = left, Y = top, Width = width, Height = height };
    }
}
