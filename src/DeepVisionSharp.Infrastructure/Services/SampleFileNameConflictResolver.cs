namespace DeepVisionSharp.Infrastructure.Services;

internal static class SampleFileNameConflictResolver
{
    public static string ResolveUniqueFileName(
        string originalName,
        string targetDirectory,
        IReadOnlySet<string> reservedNames)
    {
        var extension = Path.GetExtension(originalName);
        var baseName = Path.GetFileNameWithoutExtension(originalName);
        var fileName = string.IsNullOrWhiteSpace(baseName) ? $"image{extension}" : originalName;
        var counter = 1;

        while (reservedNames.Contains(fileName)
            || File.Exists(Path.Combine(targetDirectory, fileName)))
        {
            fileName = $"{baseName}_{counter}{extension}";
            counter++;
        }

        return fileName;
    }
}
