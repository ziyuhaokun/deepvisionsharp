namespace RuntimePackage.Engine.Models;

public sealed class InstallationRecord
{
    public string PackageId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public string EntryPointRelativePath { get; set; } = string.Empty;
    public string? StartMenuShortcutPath { get; set; }
    public string? DesktopShortcutPath { get; set; }
    public string? UninstallRegistryKeyName { get; set; }
    public string? InstallerExecutablePath { get; set; }
    public string? UninstallExecutablePath { get; set; }
    public string? UninstallCommand { get; set; }
    public string? QuietUninstallCommand { get; set; }
    public string? InstallLogPath { get; set; }
    public string UninstallDisplayName { get; set; } = string.Empty;
    public List<InstalledEnvironmentVariableRecord> EnvironmentVariables { get; set; } = [];
    public DateTime InstalledAtUtc { get; set; }
}
