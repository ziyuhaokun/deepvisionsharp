using System.IO;
using System.Text.Json;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Persists labeling canvas corner float pin state (preview: localStorage key dv-lb-float-pin).
/// </summary>
public static class LabelingCanvasFloatPreferences
{
    private const string FileName = "labeling-canvas-floats.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Dictionary<string, bool> DefaultPins = new(StringComparer.OrdinalIgnoreCase)
    {
        ["tl"] = false,
        ["tr"] = false,
        ["bl"] = false
    };

    public static bool GetPin(string pinId)
    {
        var pins = LoadPins();
        return pins.TryGetValue(pinId, out var pinned) && pinned;
    }

    public static void SetPin(string pinId, bool pinned)
    {
        var pins = LoadPins();
        pins[pinId] = pinned;
        SavePins(pins);
    }

    private static Dictionary<string, bool> LoadPins()
    {
        try
        {
            var path = GetFilePath();
            if (!File.Exists(path))
            {
                return new Dictionary<string, bool>(DefaultPins, StringComparer.OrdinalIgnoreCase);
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, bool>>(json, SerializerOptions);
            if (loaded is null)
            {
                return new Dictionary<string, bool>(DefaultPins, StringComparer.OrdinalIgnoreCase);
            }

            var merged = new Dictionary<string, bool>(DefaultPins, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in loaded)
            {
                merged[pair.Key] = pair.Value;
            }

            return merged;
        }
        catch
        {
            return new Dictionary<string, bool>(DefaultPins, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SavePins(Dictionary<string, bool> pins)
    {
        try
        {
            var path = GetFilePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(pins, SerializerOptions));
        }
        catch
        {
            // Non-critical UI preference; ignore persistence failures.
        }
    }

    private static string GetFilePath()
    {
        return Path.Combine(
            ShellRuntimePaths.Current.SettingsDirectory,
            FileName);
    }
}
