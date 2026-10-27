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

    public static IReadOnlyList<string> ReadLabels(string? documentJson)
    {
        var imageClass = ReadImageClass(documentJson);
        return string.IsNullOrWhiteSpace(imageClass) ? [] : [imageClass];
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

    private static string? ReadImageClass(IDictionary<string, JsonElement>? flags)
    {
        if (flags is null || !flags.TryGetValue(AnnotationClassLabels.ImageClassFlagKey, out var value))
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
    }
}
