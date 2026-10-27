using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeepVisionSharp.Shell.Services;

public enum ShellThemePreference
{
    System,
    Light,
    Dark
}

public sealed class ShellSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ShellThemePreference Theme { get; set; } = ShellThemePreference.System;

    public bool ShowNotifications { get; set; } = true;
}

public sealed class ShellSettingsService
{
    private const string SharedSettingsFileName = "appsettings.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly ILogger<ShellSettingsService> _logger;
    private readonly string _settingsPath;
    private readonly string? _legacySettingsPath;
    private readonly ShellRuntimePaths _paths;
    private ShellSettings _settings;

    public ShellSettingsService(ILogger<ShellSettingsService> logger, ShellRuntimePaths? paths = null)
    {
        _logger = logger;
        _paths = paths ?? ShellRuntimePaths.Current;
        _settingsPath = Path.Combine(_paths.SettingsDirectory, SharedSettingsFileName);
        _legacySettingsPath = _paths.LegacySettingsPath;
        _settings = LoadSettings();
    }

    public ShellSettings Settings => _settings;

    public string AppDataDirectory => _paths.DataDirectory;

    public string GalleryDataDirectory => Path.Combine(AppDataDirectory, "projects");

    public string LogDirectory => _paths.LogDirectory;

    public string CrashDumpDirectory => _paths.CrashDumpDirectory;

    public string ModelsDirectory => _paths.ModelsDirectory;

    public void Save(ShellSettings settings)
    {
        _settings = settings;
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);

        // Write to a sibling temp file and rename over the target. A plain WriteAllText that is
        // interrupted leaves a truncated JSON file, which the loader then treats as corrupt and
        // silently replaces with defaults — the user loses theme, notification and other settings
        // without any message.
        var tempPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_settings, SerializerOptions));
            if (File.Exists(_settingsPath))
            {
                File.Replace(tempPath, _settingsPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, _settingsPath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort; the rename either happened or the next save overwrites.
            }
        }

        _logger.LogInformation("Shell settings saved to {SettingsPath}", _settingsPath);
    }

    private ShellSettings LoadSettings()
    {
        foreach (var candidatePath in new[] { _settingsPath, _legacySettingsPath })
        {
            if (candidatePath is null) continue;
            var loaded = TryReadSettings(candidatePath);
            if (loaded is not null)
            {
                if (!string.Equals(candidatePath, _settingsPath, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Migrated shell settings from legacy path {LegacyPath}", candidatePath);
                    Save(loaded);
                }

                return loaded;
            }
        }

        var defaultSettings = new ShellSettings();
        Save(defaultSettings);
        return defaultSettings;
    }

    private ShellSettings? TryReadSettings(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ShellSettings>(json, SerializerOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load shell settings from {SettingsPath}", path);
            return null;
        }
    }

}
