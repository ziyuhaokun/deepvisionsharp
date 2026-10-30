using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Inference;

/// <summary>
/// Merges predicted detection boxes into an annotation document.
/// Append keeps existing boxes and drops predictions overlapping a same-class box at IoU &gt; 0.7;
/// Replace overwrites the box list. An empty prediction leaves the sample untouched.
/// </summary>
public static class DetectionAnnotationMerger
{
    private const double DuplicateIou = 0.7;

    public static Result<AnnotationDocument?> Merge(
        AnnotationDocument existing,
        IReadOnlyList<DetectionBoxResult> predictions,
        IReadOnlyCollection<string> knownLabels,
        bool append,
        double imageWidth,
        double imageHeight)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return Result<AnnotationDocument?>.Failure("推理结果缺少图像尺寸，已跳过写回");
        }

        var known = knownLabels.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var predicted = new List<AnnotationShape>();
        foreach (var box in predictions)
        {
            var name = string.IsNullOrWhiteSpace(box.ClassName) ? box.Label : box.ClassName;
            if (string.IsNullOrWhiteSpace(name) || !known.Contains(name.Trim()))
            {
                continue;
            }

            var shape = new AnnotationShape(
                name.Trim(),
                AnnotationShapeKind.Rectangle,
                box.X,
                box.Y,
                box.Width,
                box.Height).ClampTo(imageWidth, imageHeight);
            if (shape is not null)
            {
                predicted.Add(shape);
            }
        }

        if (predicted.Count == 0)
        {
            return Result<AnnotationDocument?>.Success(null);
        }

        var shapes = append
            ? existing.Boxes.Concat(predicted.Where(candidate =>
                !existing.Boxes.Any(current =>
                    string.Equals(current.Label, candidate.Label, StringComparison.OrdinalIgnoreCase)
                    && IntersectionOverUnion(current, candidate) > DuplicateIou))).ToArray()
            : predicted.ToArray();

        return Result<AnnotationDocument?>.Success(new AnnotationDocument(
            imageWidth,
            imageHeight,
            append ? existing.ImageClass : null,
            shapes,
            IsReviewed: true));
    }

    public static double IntersectionOverUnion(AnnotationShape first, AnnotationShape second)
    {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.Right, second.Right);
        var bottom = Math.Min(first.Bottom, second.Bottom);
        var intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        if (intersection <= 0)
        {
            return 0;
        }

        var union = (first.Width * first.Height) + (second.Width * second.Height) - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}
