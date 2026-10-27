using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed record PayloadDescriptor(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("sizeBytes")] long SizeBytes);
