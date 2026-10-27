namespace DeepVisionSharp.Shell.Services;

using System.IO;

public sealed class TrainingPreviewService
{
    public IReadOnlyList<string> GetPreviewImagePaths(string? outputRoot)
    {
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            return [];
        }

        var previewDir = Path.Combine(outputRoot, "preview");
        if (!Directory.Exists(previewDir))
        {
            return [];
        }

        return Directory.GetFiles(previewDir, "preview_*.jpg")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
