namespace DeepVisionSharp.Infrastructure.Services;

internal static class ModelArtifactPaths
{
    public static string ResolveDestinationPath(
        string modelsDirectory,
        string modelName,
        string extension,
        DateTime timestamp)
    {
        var normalizedExtension = extension.StartsWith('.') ? extension : "." + extension;
        var safeModelName = SanitizeSegment(modelName);
        var fileName = $"{safeModelName}_{timestamp:yyyyMMdd_HHmmss}{normalizedExtension}";
        return EnsureUniquePath(Path.Combine(modelsDirectory, fileName));
    }

    public static string SanitizeSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string((value ?? string.Empty).Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "model" : cleaned;
    }

    public static string EnsureUniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var index = 1;

        string candidate;
        do
        {
            candidate = Path.Combine(directory, $"{name}_{index}{extension}");
            index++;
        } while (File.Exists(candidate));

        return candidate;
    }

    public static async Task<string?> CopySidecarIfExistsAsync(
        string sourceCheckpointPath,
        string destinationCheckpointPath,
        CancellationToken cancellationToken)
    {
        var sourceInfoPath = sourceCheckpointPath + ".info";
        if (!File.Exists(sourceInfoPath))
        {
            return null;
        }

        var metadataPath = destinationCheckpointPath + ".info";
        await Task.Run(
            () => File.Copy(sourceInfoPath, metadataPath, overwrite: true),
            cancellationToken);

        return metadataPath;
    }
}
