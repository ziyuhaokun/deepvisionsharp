using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed record EntryPointDescriptor(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("arguments")] string Arguments);
