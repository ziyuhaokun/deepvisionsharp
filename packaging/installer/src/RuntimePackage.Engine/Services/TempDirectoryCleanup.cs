namespace RuntimePackage.Engine.Services;

public static class TempDirectoryCleanup
{
    public static bool TryDeleteDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return true;
        }

        try
        {
            ClearReadOnlyAttributes(path);
            Directory.Delete(path, true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ClearReadOnlyAttributes(string root)
    {
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(value => value.Length))
            {
                ClearReadOnlyAttribute(entry);
            }

            ClearReadOnlyAttribute(root);
        }
        catch
        {
        }
    }

    private static void ClearReadOnlyAttribute(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
            }
        }
        catch
        {
        }
    }
}
