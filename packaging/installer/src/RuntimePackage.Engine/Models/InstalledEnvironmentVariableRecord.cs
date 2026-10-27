namespace RuntimePackage.Engine.Models;

public sealed class InstalledEnvironmentVariableRecord
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Target { get; set; } = "User";
    public bool HadPreviousValue { get; set; }
    public string? PreviousValue { get; set; }
    public DateTime WrittenAtUtc { get; set; }
}
