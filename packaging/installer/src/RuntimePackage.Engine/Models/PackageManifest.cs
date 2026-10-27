using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed class PackageManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("packageId")]
    public string PackageId { get; set; } = string.Empty;

    [JsonPropertyName("productName")]
    public string ProductName { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("publisher")]
    public string Publisher { get; set; } = string.Empty;

    [JsonPropertyName("runtimeType")]
    public string RuntimeType { get; set; } = string.Empty;

    [JsonPropertyName("architecture")]
    public string Architecture { get; set; } = string.Empty;

    [JsonPropertyName("requiresAdmin")]
    public bool RequiresAdmin { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAtUtc { get; set; }

    [JsonPropertyName("payload")]
    public PayloadDescriptor Payload { get; set; } = new(string.Empty, string.Empty, 0);

    [JsonPropertyName("assets")]
    public AssetDescriptor Assets { get; set; } = new(string.Empty, string.Empty);

    [JsonPropertyName("metadata")]
    public MetadataDescriptor Metadata { get; set; } = new(string.Empty, []);

    [JsonPropertyName("install")]
    public InstallOptions Install { get; set; } = new(string.Empty, false, false, string.Empty);

    [JsonPropertyName("environmentVariables")]
    public List<EnvironmentVariableDescriptor> EnvironmentVariables { get; set; } = [];

    [JsonPropertyName("entryPoints")]
    public List<EntryPointDescriptor> EntryPoints { get; set; } = [];

    [JsonPropertyName("hooks")]
    public HookDescriptor Hooks { get; set; } = new(null, null);
}
