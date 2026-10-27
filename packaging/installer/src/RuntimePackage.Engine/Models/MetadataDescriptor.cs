using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed record MetadataDescriptor(
    [property: JsonPropertyName("versionFile")] string VersionFile,
    [property: JsonPropertyName("requirements")] IReadOnlyList<string> Requirements);
