using RuntimePackage.Engine.Models;
using RuntimePackage.Engine.Packaging;
using RuntimePackage.Engine.Services;
using System.Runtime.Versioning;
using System.Text.Json;

namespace RuntimePackage.Engine.Installation;

[SupportedOSPlatform("windows")]
public sealed class InstallExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly ShellIntegrationService _shellIntegration;
    private readonly UninstallRegistrationService _uninstallRegistration;
    private readonly EnvironmentVariableIntegrationService _environmentVariables;

    public InstallExecutor()
        : this(new ShellIntegrationService(), new UninstallRegistrationService(), new EnvironmentVariableIntegrationService())
    {
    }

    internal InstallExecutor(
        ShellIntegrationService shellIntegration,
        UninstallRegistrationService uninstallRegistration)
        : this(shellIntegration, uninstallRegistration, new EnvironmentVariableIntegrationService())
    {
    }

    internal InstallExecutor(
        ShellIntegrationService shellIntegration,
        UninstallRegistrationService uninstallRegistration,
        EnvironmentVariableIntegrationService environmentVariables)
    {
        _shellIntegration = shellIntegration;
        _uninstallRegistration = uninstallRegistration;
        _environmentVariables = environmentVariables;
    }

    public async Task<InstallResult> InstallAsync(InstallPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var rollbackState = new RollbackState(Directory.Exists(plan.ResolvedInstallDirectory));

        using var log = InstallLog.Open(plan.InstallLogPath);
        log.Info($"Starting install for {plan.Package.Manifest.ProductName} {plan.Package.Manifest.Version}.");
        log.Info($"Package: {plan.Package.PackagePath}");
        log.Info($"Install directory: {plan.ResolvedInstallDirectory}");

        try
        {
            PrepareInstallDirectory(plan, rollbackState);

            log.Info("Extracting payload.");
            await PackageArchiveService.ExtractToDirectoryAsync(plan.PayloadArchivePath, plan.ResolvedInstallDirectory, cancellationToken);

            var entryPointPath = Path.Combine(plan.ResolvedInstallDirectory, plan.PrimaryEntryPointRelativePath);
            if (!File.Exists(entryPointPath))
            {
                throw new InvalidOperationException($"已安装的入口程序不存在：{entryPointPath}");
            }

            log.Info("Copying uninstaller.");
            CopyUninstaller(plan);

            log.Info("Copying branding assets.");
            CopyBrandingAssets(plan);

            var brandingIconPath = Path.Combine(plan.ResolvedInstallDirectory, "metadata", "logo.ico");
            if (!File.Exists(brandingIconPath))
            {
                throw new FileNotFoundException("品牌图标不存在，无法创建快捷方式。", brandingIconPath);
            }

                        var environmentVariableRecords = new List<InstalledEnvironmentVariableRecord>();
            if (plan.RegisterEnvironmentVariables)
            {
                log.Info("Writing environment variables.");
                environmentVariableRecords = _environmentVariables.ApplyEnvironmentVariables(
                    ResolveEnvironmentVariableDescriptors(plan.Package.Manifest),
                    plan.ResolvedInstallDirectory).ToList();
                rollbackState.EnvironmentVariables = environmentVariableRecords.ToList();
            }
            else
            {
                log.Info("Skipping environment variable registration per install plan.");
            }

            log.Info("Creating shortcuts.");
            var shortcuts = _shellIntegration.CreateShortcuts(
                plan.Package.Manifest.ProductName,
                plan.ResolvedInstallDirectory,
                plan.PrimaryEntryPointRelativePath,
                plan.CreateStartMenuShortcut,
                plan.CreateDesktopShortcut,
                plan.PrimaryEntryPointArguments,
                brandingIconPath);
            rollbackState.StartMenuShortcutPath = shortcuts.StartMenuShortcutPath;
            rollbackState.DesktopShortcutPath = shortcuts.DesktopShortcutPath;

            log.Info("Writing uninstall registration.");
            _uninstallRegistration.WriteCurrentUserRegistration(
                plan.UninstallRegistryKeyName,
                plan.Package.Manifest.Install.UninstallDisplayName,
                plan.Package.Manifest.Version,
                plan.Package.Manifest.Publisher,
                plan.ResolvedInstallDirectory,
                brandingIconPath,
                plan.UninstallCommand,
                plan.QuietUninstallCommand,
                GetDirectorySize(plan.ResolvedInstallDirectory));
            rollbackState.UninstallRegistryKeyName = plan.UninstallRegistryKeyName;

            var record = new InstallationRecord
            {
                PackageId = plan.Package.Manifest.PackageId,
                ProductName = plan.Package.Manifest.ProductName,
                Version = plan.Package.Manifest.Version,
                Publisher = plan.Package.Manifest.Publisher,
                InstallDirectory = plan.ResolvedInstallDirectory,
                EntryPointRelativePath = plan.PrimaryEntryPointRelativePath,
                StartMenuShortcutPath = shortcuts.StartMenuShortcutPath,
                DesktopShortcutPath = shortcuts.DesktopShortcutPath,
                UninstallRegistryKeyName = plan.UninstallRegistryKeyName,
                InstallerExecutablePath = plan.InstallerExecutablePath,
                UninstallExecutablePath = plan.UninstallExecutablePath,
                UninstallCommand = plan.UninstallCommand,
                QuietUninstallCommand = plan.QuietUninstallCommand,
                InstallLogPath = plan.InstallLogPath,
                UninstallDisplayName = plan.Package.Manifest.Install.UninstallDisplayName,
                EnvironmentVariables = environmentVariableRecords.ToList(),
                InstalledAtUtc = DateTime.UtcNow
            };

            log.Info("Writing installation record.");
            await WriteInstallationRecordAsync(plan, record, cancellationToken);
            rollbackState.InstallationRecordPath = plan.InstallationRecordPath;

            log.Info("Install completed successfully.");

            return new InstallResult(
                plan.ResolvedInstallDirectory,
                plan.InstallationRecordPath,
                plan.InstallLogPath,
                shortcuts.StartMenuShortcutPath is not null,
                shortcuts.DesktopShortcutPath is not null,
                true,
                environmentVariableRecords.Count > 0);
        }
        catch (Exception ex)
        {
            log.Error("Install failed.", ex);
            var preservedLogPath = PreserveFailureLog(plan, log);
            log.UsePreservedPathOnly(preservedLogPath);
            Rollback(plan, rollbackState, log);

            if (!string.IsNullOrWhiteSpace(preservedLogPath))
            {
                throw new InvalidOperationException(
                    $"安装失败：{ex.Message}。已尝试回滚。失败日志：{preservedLogPath}",
                    ex);
            }

            throw;
        }
    }

    private static void PrepareInstallDirectory(InstallPlan plan, RollbackState rollbackState)
    {
        if (rollbackState.InstallDirectoryExistedBefore &&
            Directory.EnumerateFileSystemEntries(plan.ResolvedInstallDirectory).Any())
        {
            if (!plan.AllowOverwriteExistingContents)
            {
                throw new InvalidOperationException("安装目录不是空目录。");
            }

            ClearDirectoryContents(plan.ResolvedInstallDirectory);
        }

        Directory.CreateDirectory(plan.ResolvedInstallDirectory);
    }

    private static void ClearDirectoryContents(string directory)
    {
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            ClearReadOnlyAttributes(path);

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                continue;
            }

            if (File.Exists(path))
            {
                ClearReadOnlyAttribute(path);
                File.Delete(path);
            }
        }
    }

    private static async Task WriteInstallationRecordAsync(
        InstallPlan plan,
        InstallationRecord record,
        CancellationToken cancellationToken)
    {
        var installationJson = JsonSerializer.Serialize(record, JsonOptions);
        var tempInstallationRecordPath = Path.Combine(
            plan.ResolvedInstallDirectory,
            $".installation-{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(tempInstallationRecordPath, installationJson, cancellationToken);
            File.Move(tempInstallationRecordPath, plan.InstallationRecordPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempInstallationRecordPath))
            {
                File.Delete(tempInstallationRecordPath);
            }
        }
    }

    private static void CopyUninstaller(InstallPlan plan)
    {
        if (!File.Exists(plan.InstallerExecutablePath))
        {
            throw new FileNotFoundException("安装器可执行文件不存在，无法写入卸载程序。", plan.InstallerExecutablePath);
        }

        // Single-file setup embeds .vrp after the host; Uninstall.exe only needs the host.
        if (TryGetEmbeddedHostLength(plan.InstallerExecutablePath, out var hostLength))
        {
            CopyFilePrefix(plan.InstallerExecutablePath, plan.UninstallExecutablePath, hostLength);
            return;
        }

        File.Copy(plan.InstallerExecutablePath, plan.UninstallExecutablePath, overwrite: true);
    }

    private static bool TryGetEmbeddedHostLength(string setupExecutablePath, out long hostLength)
    {
        hostLength = 0;
        var info = new FileInfo(setupExecutablePath);
        if (!info.Exists || info.Length <= SetupBundleFooter.Length)
        {
            return false;
        }

        try
        {
            using var source = new FileStream(setupExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            source.Seek(-SetupBundleFooter.Length, SeekOrigin.End);
            var footerBytes = new byte[SetupBundleFooter.Length];
            source.ReadExactly(footerBytes);
            var footer = SetupBundleFooter.FromBytes(footerBytes);
            if (footer.PayloadOffset + footer.PayloadLength + SetupBundleFooter.Length != info.Length)
            {
                return false;
            }

            hostLength = footer.PayloadOffset;
            return hostLength > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void CopyFilePrefix(string sourcePath, string destinationPath, long length)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[131072];
        long remaining = length;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer.AsSpan(0, toRead));
            if (read == 0)
            {
                throw new EndOfStreamException("安装器主机部分读取不完整。");
            }

            destination.Write(buffer.AsSpan(0, read));
            remaining -= read;
        }
    }

    private static void CopyBrandingAssets(InstallPlan plan)
    {
        var metadataDirectory = Path.Combine(plan.ResolvedInstallDirectory, "metadata");
        Directory.CreateDirectory(metadataDirectory);

        var sourceIcoPath = Path.Combine(plan.Package.ExtractedRoot, "assets", "logo.ico");
        if (File.Exists(sourceIcoPath))
        {
            File.Copy(sourceIcoPath, Path.Combine(metadataDirectory, "logo.ico"), overwrite: true);
        }

        var sourceLogoPath = Path.Combine(plan.Package.ExtractedRoot, "assets", "logo.png");
        if (File.Exists(sourceLogoPath))
        {
            File.Copy(sourceLogoPath, Path.Combine(metadataDirectory, "logo.png"), overwrite: true);
        }
    }

    private void Rollback(InstallPlan plan, RollbackState rollbackState, InstallLog log)
    {
        log.Info("Starting rollback.");

        TryRollbackStep(log, "Removing shortcuts.", () =>
            _shellIntegration.RemoveShortcuts(rollbackState.StartMenuShortcutPath, rollbackState.DesktopShortcutPath));

        TryRollbackStep(log, "Removing uninstall registration.", () =>
            _uninstallRegistration.RemoveCurrentUserRegistration(rollbackState.UninstallRegistryKeyName));

        TryRollbackStep(log, "Restoring environment variables.", () =>
            _environmentVariables.RestoreEnvironmentVariables(rollbackState.EnvironmentVariables));

        TryRollbackStep(log, "Removing installation files.", () =>
        {
            if (!Directory.Exists(plan.ResolvedInstallDirectory))
            {
                return;
            }

            ClearReadOnlyAttributes(plan.ResolvedInstallDirectory);
            Directory.Delete(plan.ResolvedInstallDirectory, true);

            if (rollbackState.InstallDirectoryExistedBefore)
            {
                Directory.CreateDirectory(plan.ResolvedInstallDirectory);
            }
        });

        log.Info("Rollback finished.");
    }

    private static void TryRollbackStep(InstallLog log, string message, Action action)
    {
        try
        {
            log.Info(message);
            action();
        }
        catch (Exception ex)
        {
            log.Error("Rollback step failed.", ex);
        }
    }

    private static string? PreserveFailureLog(InstallPlan plan, InstallLog log)
    {
        try
        {
            log.Flush();
            var logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DeepVisionSharp",
                "Logs");
            Directory.CreateDirectory(logDirectory);

            var packageId = SanitizeFileName(plan.Package.Manifest.PackageId);
            var preservedPath = Path.Combine(
                logDirectory,
                $"{packageId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.install-failed.log");

            var snapshot = log.GetSnapshot();
            if (!string.IsNullOrWhiteSpace(snapshot))
            {
                File.WriteAllText(preservedPath, snapshot);
                return preservedPath;
            }

            if (File.Exists(plan.InstallLogPath))
            {
                File.Copy(plan.InstallLogPath, preservedPath, overwrite: true);
                return preservedPath;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }

    private static IReadOnlyList<EnvironmentVariableDescriptor> ResolveEnvironmentVariableDescriptors(PackageManifest manifest)
    {
        if (manifest.EnvironmentVariables.Count > 0)
        {
            return manifest.EnvironmentVariables;
        }

        return string.Equals(manifest.RuntimeType, "python", StringComparison.OrdinalIgnoreCase)
            ? [new EnvironmentVariableDescriptor("In3Vision_Home", "{installDir}\\python.exe", "User")]
            : [];
    }

    private static long GetDirectorySize(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length);
        }
        catch
        {
            return 0;
        }
    }

    private static void ClearReadOnlyAttributes(string root)
    {
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
                         .OrderByDescending(value => value.Length))
            {
                ClearReadOnlyAttribute(path);
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

    private sealed class RollbackState(bool installDirectoryExistedBefore)
    {
        public bool InstallDirectoryExistedBefore { get; } = installDirectoryExistedBefore;
        public string? StartMenuShortcutPath { get; set; }
        public string? DesktopShortcutPath { get; set; }
        public string? UninstallRegistryKeyName { get; set; }
        public string? InstallationRecordPath { get; set; }
        public List<InstalledEnvironmentVariableRecord> EnvironmentVariables { get; set; } = [];
    }

    private sealed class InstallLog : IDisposable
    {
        private readonly string _path;
        private readonly List<string> _lines = [];
        private readonly object _sync = new();
        private string? _preservedPath;
        private bool _writePrimary = true;

        private InstallLog(string path)
        {
            _path = path;
        }

        public static InstallLog Open(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return new InstallLog(path);
        }

        public void Info(string message) =>
            Write("INF", message);

        public void Error(string message, Exception exception) =>
            Write("ERR", $"{message} {exception.GetType().Name}: {exception.Message}");

        public void UsePreservedPathOnly(string? preservedPath)
        {
            _preservedPath = preservedPath;
            _writePrimary = false;
        }

        public string GetSnapshot()
        {
            lock (_sync)
            {
                return string.Concat(_lines);
            }
        }

        public void Flush() =>
            _ = true;

        public void Dispose() =>
            _ = true;


        private void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

            lock (_sync)
            {
                _lines.Add(line);
            }

            if (_writePrimary)
            {
                AppendLine(_path, line);
            }

            if (!string.IsNullOrWhiteSpace(_preservedPath))
            {
                AppendLine(_preservedPath, line);
            }
        }

        private static void AppendLine(string path, string line)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line);
        }
    }
}
