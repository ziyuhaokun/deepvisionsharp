using System.Text.Json;
using System.Text.Json.Serialization;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Labelme JSON document serialization for image-class annotations.
/// DTO is the product internal contract (with the non-standard imageClass flag).
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

    public static Dictionary<string, object> BuildDocumentFlags(string? imageClass)
    {
        var flags = new Dictionary<string, object>();
        if (!string.IsNullOrWhiteSpace(imageClass))
        {
            flags[AnnotationClassLabels.ImageClassFlagKey] = imageClass.Trim();
        }

        return flags;
    }

    public static string? ReadImageClass(IDictionary<string, object>? flags)
    {
        if (flags is null || !flags.TryGetValue(AnnotationClassLabels.ImageClassFlagKey, out var value))
        {
            return null;
        }

        var text = value switch
        {
            string s => s,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
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

    [JsonPropertyName("imagePath")]
    public string ImagePath { get; set; } = string.Empty;

    [JsonPropertyName("imageData")]
    public string? ImageData { get; set; }

    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }
}
