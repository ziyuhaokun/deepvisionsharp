using System.Text.Json;

namespace DeepVisionSharp.Infrastructure.Services;

internal static class TrainingCompletionSignals
{
    public const string CompletedPrefix = "DV_COMPLETED ";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static bool TryParseCompleted(
        string line,
        string workingDirectory,
        out TrainingCompletedPayload? payload)
    {
        payload = null;
        var json = ExtractPayload(line, CompletedPrefix);
        if (json is null)
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<TrainingCompletedPayload>(json, JsonOptions);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.BestModelPath))
            {
                return false;
            }

            var bestModelPath = ResolvePath(parsed.BestModelPath, workingDirectory);
            var metadataPath = string.IsNullOrWhiteSpace(parsed.MetadataPath)
                ? bestModelPath + ".info"
                : ResolvePath(parsed.MetadataPath, workingDirectory);
            string? lastModelPath = null;
            if (!string.IsNullOrWhiteSpace(parsed.LastModelPath))
            {
                lastModelPath = ResolvePath(parsed.LastModelPath, workingDirectory);
            }

            payload = parsed with
            {
                BestModelPath = bestModelPath,
                MetadataPath = metadataPath,
                LastModelPath = lastModelPath
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ExtractPayload(string line, string prefix)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var index = line.IndexOf(prefix, StringComparison.Ordinal);
        return index < 0 ? null : line[(index + prefix.Length)..].Trim();
    }

    private static string ResolvePath(string path, string workingDirectory)
        => Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(path, workingDirectory);
}

internal sealed record TrainingCompletedPayload(
    string BestModelPath,
    string MetadataPath,
    string? LastModelPath,
    string? DatasetFingerprint,
    string SpatialMode);
