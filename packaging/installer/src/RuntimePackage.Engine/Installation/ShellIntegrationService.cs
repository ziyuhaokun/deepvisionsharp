using System.Runtime.Versioning;

namespace RuntimePackage.Engine.Installation;

public sealed record ShortcutIntegrationResult(string? StartMenuShortcutPath, string? DesktopShortcutPath);

[SupportedOSPlatform("windows")]
public class ShellIntegrationService
{
    private readonly string _startMenuProgramsDirectory;
    private readonly string _desktopDirectory;

    public ShellIntegrationService(
        string? startMenuProgramsDirectory = null,
        string? desktopDirectory = null)
    {
        _startMenuProgramsDirectory = startMenuProgramsDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft",
                "Windows",
                "Start Menu",
                "Programs");
        _desktopDirectory = desktopDirectory
            ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    public virtual ShortcutIntegrationResult CreateShortcuts(
        string productName,
        string installDirectory,
        string entryPointRelativePath,
        bool createStartMenuShortcut,
        bool createDesktopShortcut,
        string? arguments = null,
        string? iconPath = null)
    {
        var targetPath = Path.Combine(installDirectory, entryPointRelativePath);
        if (!File.Exists(targetPath))
        {
            throw new InvalidOperationException($"入口程序不存在：{targetPath}");
        }

        var resolvedIconPath = ResolveIconPath(iconPath, targetPath);

        string? startMenuShortcutPath = null;
        string? desktopShortcutPath = null;

        try
        {
            if (createStartMenuShortcut)
            {
                Directory.CreateDirectory(_startMenuProgramsDirectory);
                startMenuShortcutPath = Path.Combine(_startMenuProgramsDirectory, productName + ".lnk");
                CreateShortcut(
                    startMenuShortcutPath,
                    targetPath,
                    installDirectory,
                    productName,
                    arguments,
                    resolvedIconPath);
            }

            if (createDesktopShortcut)
            {
                Directory.CreateDirectory(_desktopDirectory);
                desktopShortcutPath = Path.Combine(_desktopDirectory, productName + ".lnk");
                CreateShortcut(
                    desktopShortcutPath,
                    targetPath,
                    installDirectory,
                    productName,
                    arguments,
                    resolvedIconPath);
            }

            return new ShortcutIntegrationResult(startMenuShortcutPath, desktopShortcutPath);
        }
        catch
        {
            RemoveShortcuts(startMenuShortcutPath, desktopShortcutPath);
            throw;
        }
    }

    public virtual void RemoveShortcuts(string? startMenuShortcutPath, string? desktopShortcutPath)
    {
        DeleteIfExists(startMenuShortcutPath);
        DeleteIfExists(desktopShortcutPath);
    }

    private static string ResolveIconPath(string? iconPath, string targetPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
        {
            return iconPath;
        }

        return targetPath;
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string description,
        string? arguments,
        string iconPath)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法创建 Windows Shell 快捷方式。");
        dynamic? shell = null;
        dynamic? shortcut = null;

        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shell!.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Description = description;
            // WScript.Shell expects "path,index"; index 0 is the primary icon resource.
            shortcut.IconLocation = $"{iconPath},0";
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                shortcut.Arguments = arguments;
            }
            shortcut.Save();
        }
        finally
        {
            ReleaseComObject(shortcut);
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value is not null && System.Runtime.InteropServices.Marshal.IsComObject(value))
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
        }
    }
}
