using System.Text.Json;
using System.Text.Json.Serialization;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Labelme JSON document serialization. DTO is the product internal contract: the image class
/// lives in <c>flags.imageClass</c>, detection boxes in the standard <c>shapes</c> array, and
/// <c>flags.dvReviewed</c> marks a detection sample as inspected (including empty negatives).
/// </summary>
internal static class LabelmeAnnotationSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static LabelmeDocument? Deserialize(string documentJson)
        => JsonSerializer.Deserialize<LabelmeDocument>(documentJson, JsonOptions);

    public static string Serialize(LabelmeDocument document)
        => JsonSerializer.Serialize(document, JsonOptions);

    public static Dictionary<string, object> BuildDocumentFlags(string? imageClass, bool reviewed = false)
    {
        var flags = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(imageClass))
        {
            flags[AnnotationClassLabels.ImageClassFlagKey] = imageClass.Trim();
        }

        if (reviewed)
        {
            flags[AnnotationClassLabels.ReviewedFlagKey] = true;
        }

        return flags;
    }

    public static string? ReadImageClass(IDictionary<string, object>? flags)
        => ReadFlag(flags, AnnotationClassLabels.ImageClassFlagKey);

    public static bool ReadReviewed(IDictionary<string, object>? flags)
    {
        var text = ReadFlag(flags, AnnotationClassLabels.ReviewedFlagKey);
        return bool.TryParse(text, out var reviewed) && reviewed;
    }

    public static List<LabelmeShape> ToLabelmeShapes(IReadOnlyList<AnnotationShape>? shapes)
    {
        if (shapes is null || shapes.Count == 0)
        {
            return new List<LabelmeShape>();
        }

        return shapes
            .Where(shape => shape.HasArea)
            .Select(shape => new LabelmeShape
            {
                Label = shape.Label.Trim(),
                Points =
                [
                    [shape.X, shape.Y],
                    [shape.Right, shape.Bottom]
                ],
                ShapeType = "rectangle"
            })
            .ToList();
    }

    public static IReadOnlyList<AnnotationShape> ReadShapes(LabelmeDocument? document)
    {
        if (document?.Shapes is not { Count: > 0 })
        {
            return Array.Empty<AnnotationShape>();
        }

        var shapes = new List<AnnotationShape>();
        foreach (var shape in document.Shapes)
        {
            if (string.IsNullOrWhiteSpace(shape.Label) || shape.Points is not { Count: >= 2 })
            {
                continue;
            }

            var first = shape.Points[0];
            var second = shape.Points[^1];
            if (first is not { Length: >= 2 } || second is not { Length: >= 2 })
            {
                continue;
            }

            var candidate = new AnnotationShape(
                shape.Label.Trim(),
                AnnotationShapeKind.Rectangle,
                Math.Min(first[0], second[0]),
                Math.Min(first[1], second[1]),
                Math.Abs(second[0] - first[0]),
                Math.Abs(second[1] - first[1]));
            if (candidate.HasArea)
            {
                shapes.Add(candidate);
            }
        }

        return shapes;
    }

    private static string? ReadFlag(IDictionary<string, object>? flags, string key)
    {
        if (flags is null || !flags.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value switch
        {
            string s => s,
            bool b => b.ToString(),
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element when element.ValueKind is JsonValueKind.True or JsonValueKind.False => element.GetBoolean().ToString(),
            _ => value?.ToString()
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

internal sealed class LabelmeDocument
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("flags")]
    public IDictionary<string, object> Flags { get; set; } = new Dictionary<string, object>();

    [JsonPropertyName("shapes")]
    public List<LabelmeShape> Shapes { get; set; } = new();

    [JsonPropertyName("imagePath")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("imageData")]
    public string? ImageData { get; set; }

    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }
}

internal sealed class LabelmeShape
{
    [JsonPropertyName("label")]
    public string Label { get; set; } = string.Empty;

    /// <summary>Corner points in image pixels: top-left and bottom-right for a rectangle.</summary>
    [JsonPropertyName("points")]
    public List<double[]> Points { get; set; } = new();

    [JsonPropertyName("group_id")]
    public int? GroupId { get; set; }

    [JsonPropertyName("shape_type")]
    public string ShapeType { get; set; } = "rectangle";

    [JsonPropertyName("flags")]
    public Dictionary<string, object> Flags { get; set; } = new();
}
