namespace DeepVisionSharp.Infrastructure.Services.Transfer;

/// <summary>
/// File-name rules shared by project and gallery imports/exports.
/// Kept behavior-identical to the former per-service copies; see TransferFileNamesTests.
/// </summary>
internal static class TransferFileNames
{
    /// <summary>
    /// Returns <paramref name="baseName"/> (trimmed) or the next free "名称 (导入N)" variant,
    /// registering the result in <paramref name="existingNames"/>. Blank names use the fallback.
    /// </summary>
    public static string MakeUniqueName(string? baseName, HashSet<string> existingNames, string fallbackName)
    {
        var name = string.IsNullOrWhiteSpace(baseName) ? fallbackName : baseName.Trim();
        if (existingNames.Add(name))
        {
            return name;
        }

        var index = 1;
        while (true)
        {
            var candidate = $"{name} (导入{index})";
            if (existingNames.Add(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    /// <summary>
    /// Returns <paramref name="originalName"/> when the target directory has no such file,
    /// otherwise appends "_N" before the extension ("名称_N.ext").
    /// </summary>
    public static string GenerateUniqueFileName(string targetDirectory, string originalName)
    {
        var extension = Path.GetExtension(originalName);
        var baseName = Path.GetFileNameWithoutExtension(originalName);
        var fileName = string.IsNullOrWhiteSpace(baseName) ? $"image{extension}" : originalName;
        var counter = 1;

        while (File.Exists(Path.Combine(targetDirectory, fileName)))
        {
            fileName = $"{baseName}_{counter}{extension}";
            counter++;
        }

        return fileName;
    }

    /// <summary>
    /// Validates a file name coming from an external package: only pure names are accepted
    /// (no directory separators, not rooted), so Path.Combine cannot escape the gallery directory.
    /// </summary>
    public static bool TryGetSafeFileName(string? fileName, out string safeFileName)
    {
        safeFileName = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var candidate = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(candidate)
            || !string.Equals(candidate, fileName, StringComparison.Ordinal)
            || Path.IsPathRooted(fileName))
        {
            return false;
        }

        safeFileName = candidate;
        return true;
    }
}
