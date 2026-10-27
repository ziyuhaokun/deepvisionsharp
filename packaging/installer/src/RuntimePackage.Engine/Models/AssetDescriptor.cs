using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed record AssetDescriptor(
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("logo")] string Logo);
