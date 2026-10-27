using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed class EnvironmentVariableDescriptor
{
    public EnvironmentVariableDescriptor()
    {
    }

    public EnvironmentVariableDescriptor(string name, string value, string target)
    {
        Name = name;
        Value = value;
        Target = target;
    }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = "User";
}
