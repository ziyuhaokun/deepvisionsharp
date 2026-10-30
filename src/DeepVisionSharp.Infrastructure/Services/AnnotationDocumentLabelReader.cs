using System.Text.Json;
using System.Text.Json.Serialization;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Infrastructure.Services;

internal static class AnnotationDocumentLabelReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Labels a sample is indexed under: the image class (classification) plus every distinct box
    /// label (detection). The label index, statistics and split-by-label all derive from this.
    /// </summary>
    public static IReadOnlyList<string> ReadLabels(string? documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            return [];
        }

        try
        {
            var document = JsonSerializer.Deserialize<LabelmeDocumentLite>(documentJson, JsonOptions);
            var labels = new List<string>();
            var imageClass = ReadImageClass(document?.Flags);
            if (!string.IsNullOrWhiteSpace(imageClass))
            {
                labels.Add(imageClass.Trim());
            }

            if (document?.Shapes is { Count: > 0 })
            {
                foreach (var shape in document.Shapes)
                {
                    var label = shape?.Label?.Trim();
                    if (!string.IsNullOrWhiteSpace(label)
                        && !labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                    {
                        labels.Add(label);
                    }
                }
            }

            return labels;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string? ReadImageClass(string? documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<LabelmeDocumentLite>(documentJson, JsonOptions);
            return ReadImageClass(document?.Flags);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool ReadReviewed(string? documentJson)
    {
        if (string.IsNullOrWhiteSpace(documentJson))
        {
            return false;
        }

        try
        {
            var document = JsonSerializer.Deserialize<LabelmeDocumentLite>(documentJson, JsonOptions);
            return ReadFlag(document?.Flags, AnnotationClassLabels.ReviewedFlagKey) is { } value
                && bool.TryParse(value, out var reviewed)
                && reviewed;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadImageClass(IDictionary<string, JsonElement>? flags) =>
        ReadFlag(flags, AnnotationClassLabels.ImageClassFlagKey);

    private static string? ReadFlag(IDictionary<string, JsonElement>? flags, string key)
    {
        if (flags is null || !flags.TryGetValue(key, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.ToString()
        };

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private sealed class LabelmeDocumentLite
    {
        [JsonPropertyName("flags")]
        public Dictionary<string, JsonElement>? Flags { get; set; }

        [JsonPropertyName("shapes")]
        public List<LabelmeShapeLite>? Shapes { get; set; }
    }

    private sealed class LabelmeShapeLite
    {
        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }
}
