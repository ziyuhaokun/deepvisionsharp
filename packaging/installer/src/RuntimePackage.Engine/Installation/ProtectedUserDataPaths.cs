namespace RuntimePackage.Engine.Installation;

/// <summary>
/// Protects user-owned DeepVisionSharp runtime directories (Data, Models, Logs, CrashDumps) when
/// an installer or uninstaller operates on the legacy shared %LocalAppData%\DeepVisionSharp root.
/// Custom directories chosen by the user are not treated as shared user-data roots.
/// </summary>
public static class ProtectedUserDataPaths
{
    private static readonly HashSet<string> ProtectedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data",
        "Models",
        "Logs",
        "CrashDumps"
    };

    public static string DefaultSharedUserDataRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DeepVisionSharp");

    public static bool IsSharedUserDataDirectory(string installDirectory, string? sharedUserDataRoot = null)
    {
        if (string.IsNullOrWhiteSpace(installDirectory))
        {
            return false;
        }

        var targetRoot = string.IsNullOrWhiteSpace(sharedUserDataRoot)
            ? DefaultSharedUserDataRoot
            : sharedUserDataRoot;
        if (string.IsNullOrWhiteSpace(targetRoot))
        {
            return false;
        }

        try
        {
            var expandedInstall = Environment.ExpandEnvironmentVariables(installDirectory);
            var expandedTarget = Environment.ExpandEnvironmentVariables(targetRoot);
            return string.Equals(
                NormalizePath(expandedInstall),
                NormalizePath(expandedTarget),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsProtectedTopLevelEntry(
        string installDirectory,
        string entryPath,
        string? sharedUserDataRoot = null)
    {
        if (!IsSharedUserDataDirectory(installDirectory, sharedUserDataRoot))
        {
            return false;
        }

        var installRoot = NormalizePath(installDirectory);
        var normalizedEntry = NormalizePath(entryPath);
        var parent = Path.GetDirectoryName(normalizedEntry);
        if (!string.Equals(parent, installRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = Path.GetFileName(normalizedEntry);
        return ProtectedDirectoryNames.Contains(name) && Directory.Exists(normalizedEntry);
    }

    public static bool IsProtectedDescendant(
        string installDirectory,
        string path,
        string? sharedUserDataRoot = null)
    {
        if (!IsSharedUserDataDirectory(installDirectory, sharedUserDataRoot))
        {
            return false;
        }

        var installRoot = NormalizePath(installDirectory);
        var normalizedPath = NormalizePath(path);
        var relative = Path.GetRelativePath(installRoot, normalizedPath);
        if (string.IsNullOrWhiteSpace(relative)
            || string.Equals(relative, ".", StringComparison.Ordinal)
            || relative.StartsWith("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return false;
        }

        var separatorIndex = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var topSegment = separatorIndex >= 0 ? relative[..separatorIndex] : relative;
        return ProtectedDirectoryNames.Contains(topSegment)
            && Directory.Exists(Path.Combine(installRoot, topSegment));
    }

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
