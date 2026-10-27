using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed record HookDescriptor(
    [property: JsonPropertyName("preinstall")] string? Preinstall,
    [property: JsonPropertyName("postinstall")] string? Postinstall);
