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
    private readonly string? _sharedUserDataRoot;
    private readonly string? _failureLogDirectory;

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
        EnvironmentVariableIntegrationService environmentVariables,
        string? sharedUserDataRoot = null,
        string? failureLogDirectory = null)
    {
        _shellIntegration = shellIntegration;
        _uninstallRegistration = uninstallRegistration;
        _environmentVariables = environmentVariables;
        _sharedUserDataRoot = sharedUserDataRoot;
        _failureLogDirectory = failureLogDirectory;
    }

    public async Task<InstallResult> InstallAsync(InstallPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        var directories = new InstallDirectorySwap(plan.ResolvedInstallDirectory, _sharedUserDataRoot);
        var rollbackState = new RollbackState();
        var keepBackup = false;

        using var log = InstallLog.Open(Path.Combine(directories.WorkDirectory, "install.log"));
        log.Info($"Starting install for {plan.Package.Manifest.ProductName} {plan.Package.Manifest.Version}.");
        log.Info($"Package: {plan.Package.PackagePath}");
        log.Info($"Install directory: {plan.ResolvedInstallDirectory}");

        try
        {
            log.Info("Extracting payload to staging directory.");
            var stagingDirectory = directories.StagingDirectory;
            await PackageArchiveService.ExtractToDirectoryAsync(plan.PayloadArchivePath, stagingDirectory, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var entryPointPath = Path.Combine(stagingDirectory, plan.PrimaryEntryPointRelativePath);
            if (!File.Exists(entryPointPath))
            {
                throw new InvalidOperationException($"已安装的入口程序不存在：{entryPointPath}");
            }

            log.Info("Copying uninstaller.");
            CopyUninstaller(plan.InstallerExecutablePath, Path.Combine(stagingDirectory, Path.GetFileName(plan.UninstallExecutablePath)));

            log.Info("Copying branding assets.");
            CopyBrandingAssets(plan.Package.ExtractedRoot, stagingDirectory);

            var stagedIconPath = Path.Combine(stagingDirectory, "metadata", "logo.ico");
            if (!File.Exists(stagedIconPath))
            {
                throw new FileNotFoundException("品牌图标不存在，无法创建快捷方式。", stagedIconPath);
            }

            log.Info("Switching staged program files into the install directory.");
            directories.Promote(plan.AllowOverwriteExistingContents, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var brandingIconPath = Path.Combine(plan.ResolvedInstallDirectory, "metadata", "logo.ico");
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

            cancellationToken.ThrowIfCancellationRequested();
            log.Info("Creating shortcuts.");
            rollbackState.RestoreShortcuts = _shellIntegration.CaptureRollback(
                plan.Package.Manifest.ProductName, plan.CreateStartMenuShortcut, plan.CreateDesktopShortcut);
            var shortcuts = _shellIntegration.CreateShortcuts(
                plan.Package.Manifest.ProductName,
                plan.ResolvedInstallDirectory,
                plan.PrimaryEntryPointRelativePath,
                plan.CreateStartMenuShortcut,
                plan.CreateDesktopShortcut,
                plan.PrimaryEntryPointArguments,
                brandingIconPath);
            cancellationToken.ThrowIfCancellationRequested();
            log.Info("Writing uninstall registration.");
            rollbackState.RestoreUninstallRegistration = _uninstallRegistration.CaptureRollback(plan.UninstallRegistryKeyName);
            _uninstallRegistration.WriteCurrentUserRegistration(
                plan.UninstallRegistryKeyName,
                plan.Package.Manifest.Install.UninstallDisplayName,
                plan.Package.Manifest.Version,
                plan.Package.Manifest.Publisher,
                plan.ResolvedInstallDirectory,
                brandingIconPath,
                plan.UninstallCommand,
                plan.QuietUninstallCommand,
                GetDirectorySize(plan.ResolvedInstallDirectory, _sharedUserDataRoot));

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

            log.Info("Install completed successfully.");
            log.SaveTo(plan.InstallLogPath);

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
            RestoreIntegrations(rollbackState, log);
            keepBackup = !TryRollbackStep(log, "Restoring previous program files.", directories.Rollback);
            var preservedLogPath = PreserveFailureLog(plan, log, _failureLogDirectory);
            if (keepBackup)
            {
                throw new InvalidOperationException(
                    $"安装失败：{ex.Message}。部分程序文件未能恢复，备份目录：{directories.WorkDirectory}。失败日志：{preservedLogPath}", ex);
            }

            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            if (preservedLogPath is not null)
            {
                throw new InvalidOperationException(
                    $"安装失败：{ex.Message}。已尝试回滚。失败日志：{preservedLogPath}", ex);
            }

            throw;
        }
        finally
        {
            if (!keepBackup)
            {
                TempDirectoryCleanup.TryDeleteDirectory(directories.WorkDirectory);
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

    private static void CopyUninstaller(string installerPath, string uninstallerPath)
    {
        if (!File.Exists(installerPath))
        {
            throw new FileNotFoundException("安装器可执行文件不存在，无法写入卸载程序。", installerPath);
        }

        // Single-file setup embeds .vrp after the host; Uninstall.exe only needs the host.
        if (TryGetEmbeddedHostLength(installerPath, out var hostLength))
        {
            CopyFilePrefix(installerPath, uninstallerPath, hostLength);
            return;
        }

        File.Copy(installerPath, uninstallerPath, overwrite: true);
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

    private static void CopyBrandingAssets(string extractedRoot, string stagingDirectory)
    {
        var metadataDirectory = Path.Combine(stagingDirectory, "metadata");
        Directory.CreateDirectory(metadataDirectory);

        var sourceIcoPath = Path.Combine(extractedRoot, "assets", "logo.ico");
        if (File.Exists(sourceIcoPath))
        {
            File.Copy(sourceIcoPath, Path.Combine(metadataDirectory, "logo.ico"), overwrite: true);
        }

        var sourceLogoPath = Path.Combine(extractedRoot, "assets", "logo.png");
        if (File.Exists(sourceLogoPath))
        {
            File.Copy(sourceLogoPath, Path.Combine(metadataDirectory, "logo.png"), overwrite: true);
        }
    }

    private void RestoreIntegrations(RollbackState state, InstallLog log)
    {
        if (state.RestoreUninstallRegistration is { } restoreRegistration)
        {
            TryRollbackStep(log, "Restoring uninstall registration.", restoreRegistration);
        }

        if (state.RestoreShortcuts is { } restoreShortcuts)
        {
            TryRollbackStep(log, "Restoring shortcuts.", restoreShortcuts);
        }

        TryRollbackStep(log, "Restoring environment variables.", () =>
            _environmentVariables.RestoreEnvironmentVariables(state.EnvironmentVariables));
    }

    private static bool TryRollbackStep(InstallLog log, string message, Action action)
    {
        try
        {
            log.Info(message);
            action();
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Rollback step failed.", ex);
            return false;
        }
    }

    private static string? PreserveFailureLog(
        InstallPlan plan,
        InstallLog log,
        string? failureLogDirectory)
    {
        try
        {
            var logDirectory = string.IsNullOrWhiteSpace(failureLogDirectory)
                ? Path.Combine(ProtectedUserDataPaths.DefaultSharedUserDataRoot, "Logs")
                : failureLogDirectory;
            Directory.CreateDirectory(logDirectory);

            var packageId = SanitizeFileName(plan.Package.Manifest.PackageId);
            var preservedPath = Path.Combine(
                logDirectory,
                $"{packageId}-{DateTime.Now:yyyyMMdd-HHmmssfff}.install-failed.log");

            File.WriteAllText(preservedPath, log.GetSnapshot());
            return preservedPath;
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

    internal static long GetDirectorySize(string directory, string? sharedUserDataRoot = null)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            long total = 0;
            var recurseOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true
            };

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (ProtectedUserDataPaths.IsProtectedTopLevelEntry(directory, entry, sharedUserDataRoot))
                {
                    continue;
                }

                if (File.Exists(entry))
                {
                    try { total += new FileInfo(entry).Length; } catch { }
                    continue;
                }

                if (Directory.Exists(entry))
                {
                    foreach (var file in Directory.EnumerateFiles(entry, "*", recurseOptions))
                    {
                        try { total += new FileInfo(file).Length; } catch { }
                    }
                }
            }

            return total;
        }
        catch
        {
            return 0;
        }
    }

    private sealed class RollbackState
    {
        public Action? RestoreShortcuts { get; set; }
        public Action? RestoreUninstallRegistration { get; set; }
        public List<InstalledEnvironmentVariableRecord> EnvironmentVariables { get; set; } = [];
    }

    private sealed class InstallLog : IDisposable
    {
        private readonly string _path;
        private readonly List<string> _lines = [];
        private readonly object _sync = new();

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

        public string GetSnapshot()
        {
            lock (_sync)
            {
                return string.Concat(_lines);
            }
        }

        public void SaveTo(string path)
        {
            try { File.WriteAllText(path, GetSnapshot()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose() { }

        private void Write(string level, string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

            lock (_sync)
            {
                _lines.Add(line);
            }

            AppendLine(_path, line);
        }

        private static void AppendLine(string path, string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, line);
            }
            catch
            {
                // Best-effort file logging: never abort rollback when disk is full or log is locked.
            }
        }
    }
}
