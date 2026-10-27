using System.Text.Json.Serialization;

namespace RuntimePackage.Engine.Models;

public sealed record InstallOptions(
    [property: JsonPropertyName("defaultDirectory")] string DefaultDirectory,
    [property: JsonPropertyName("startMenuShortcut")] bool StartMenuShortcut,
    [property: JsonPropertyName("desktopShortcut")] bool DesktopShortcut,
    [property: JsonPropertyName("uninstallDisplayName")] string UninstallDisplayName);
