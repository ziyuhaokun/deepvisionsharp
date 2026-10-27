namespace RuntimePackage.Engine.Installation;

public sealed record InstallPlan(
    PackageReadResult Package,
    string SelectedInstallDirectory,
    string ResolvedInstallDirectory,
    bool AllowOverwriteExistingContents,
    string PayloadArchivePath,
    string InstallationRecordPath,
    string PrimaryEntryPointRelativePath,
    string? PrimaryEntryPointArguments,
    bool CreateStartMenuShortcut,
    bool CreateDesktopShortcut,
    string UninstallRegistryKeyName,
    string InstallerExecutablePath,
    string UninstallExecutablePath,
    string UninstallCommand,
    string QuietUninstallCommand,
    string InstallLogPath,
    bool RegisterEnvironmentVariables = true);

public sealed record InstallResult(
    string InstallDirectory,
    string InstallationRecordPath,
    string InstallLogPath,
    bool StartMenuShortcutCreated = false,
    bool DesktopShortcutCreated = false,
    bool UninstallRegistered = false,
    bool EnvironmentVariablesRegistered = false);
