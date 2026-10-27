namespace RuntimePackage.Engine.Installation;

public sealed class InstallPlanner
{
    public InstallPlan CreatePlan(
        PackageReadResult package,
        string installDirectory,
        bool createStartMenuShortcut = false,
        bool createDesktopShortcut = false,
        string? installerExecutablePath = null,
        bool allowOverwriteExistingContents = false,
        bool registerEnvironmentVariables = true)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);

        var resolvedInstallDirectory = NormalizeDirectoryPath(
            Environment.ExpandEnvironmentVariables(installDirectory));
        var extractedRoot = NormalizeDirectoryPath(package.ExtractedRoot);

        if (PathsOverlap(resolvedInstallDirectory, extractedRoot))
        {
            throw new InvalidOperationException("安装目录与安装包解压目录重叠。");
        }

        var primaryEntryPoint = package.Manifest.EntryPoints.FirstOrDefault()
            ?? throw new InvalidOperationException("安装包清单未定义入口程序。");

        var payloadArchivePath = Path.Combine(extractedRoot, package.Manifest.Payload.File);
        var installationRecordPath = Path.Combine(resolvedInstallDirectory, "installation.json");
        var uninstallRegistryKeyName = BuildUninstallKeyName(package.Manifest.PackageId);
        var resolvedInstallerExecutablePath = NormalizeFilePath(
            string.IsNullOrWhiteSpace(installerExecutablePath)
                ? Environment.ProcessPath ?? throw new InvalidOperationException("无法解析安装器可执行文件路径。")
                : Environment.ExpandEnvironmentVariables(installerExecutablePath));
        var uninstallExecutablePath = Path.Combine(resolvedInstallDirectory, "Uninstall.exe");
        var uninstallCommand = BuildUninstallCommand(uninstallExecutablePath, installationRecordPath, quiet: false);
        var quietUninstallCommand = BuildUninstallCommand(uninstallExecutablePath, installationRecordPath, quiet: true);
        var installLogPath = Path.Combine(resolvedInstallDirectory, "install.log");

        var entryPointArguments = string.IsNullOrWhiteSpace(primaryEntryPoint.Arguments)
            ? null
            : primaryEntryPoint.Arguments;

        return new InstallPlan(
            package,
            installDirectory,
            resolvedInstallDirectory,
            allowOverwriteExistingContents,
            payloadArchivePath,
            installationRecordPath,
            primaryEntryPoint.RelativePath,
            entryPointArguments,
            createStartMenuShortcut,
            createDesktopShortcut,
            uninstallRegistryKeyName,
            resolvedInstallerExecutablePath,
            uninstallExecutablePath,
            uninstallCommand,
            quietUninstallCommand,
            installLogPath,
            registerEnvironmentVariables);
    }

    private static string BuildUninstallKeyName(string packageId) =>
        packageId.Replace(".", "_", StringComparison.OrdinalIgnoreCase) + "_CurrentUser";

    private static string NormalizeDirectoryPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string NormalizeFilePath(string path) =>
        Path.GetFullPath(path);

    private static string BuildUninstallCommand(string installerExecutablePath, string installationRecordPath, bool quiet)
    {
        var command = $"{Quote(installerExecutablePath)} --uninstall {Quote(installationRecordPath)}";
        return quiet ? command + " --quiet" : command;
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static bool PathsOverlap(string left, string right) =>
        IsSameOrUnder(left, right) || IsSameOrUnder(right, left);

    private static bool IsSameOrUnder(string candidate, string parent)
    {
        if (string.Equals(candidate, parent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parentPrefix = parent.EndsWith(Path.DirectorySeparatorChar)
            ? parent
            : parent + Path.DirectorySeparatorChar;

        return candidate.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
