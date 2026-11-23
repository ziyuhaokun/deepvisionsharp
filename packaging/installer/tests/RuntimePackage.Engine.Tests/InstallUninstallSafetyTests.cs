using System.Runtime.Versioning;
using RuntimePackage.Engine.Installation;
using RuntimePackage.Engine.Models;
using RuntimePackage.Engine.Packaging;
using RuntimePackage.Engine.Services;
using Xunit;

namespace RuntimePackage.Engine.Tests;

[SupportedOSPlatform("windows")]
public sealed class InstallUninstallSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "RuntimePackage.SafetyTests", Guid.NewGuid().ToString("N"));

    public InstallUninstallSafetyTests() => Directory.CreateDirectory(_root);

    public void Dispose() => TempDirectoryCleanup.TryDeleteDirectory(_root);

    [Fact]
    public void DefaultInstallDirectory_is_separated_from_user_data_directory()
    {
        Assert.Equal("%LocalAppData%\\Programs\\DeepVisionSharp", new PackagerOptions().DefaultInstallDirectory);
    }

    [Fact]
    public async Task Install_into_existing_empty_directory_succeeds_without_overwrite_flag()
    {
        var (packagePath, installerExePath) = await BuildTestPackageAsync();
        var installDir = Path.Combine(_root, "empty-install");
        Directory.CreateDirectory(installDir);

        var package = await new PackageReader().OpenAsync(packagePath, CancellationToken.None);
        try
        {
            var plan = new InstallPlanner().CreatePlan(
                package,
                installDir,
                createStartMenuShortcut: false,
                createDesktopShortcut: false,
                installerExecutablePath: installerExePath,
                allowOverwriteExistingContents: false,
                registerEnvironmentVariables: false);

            var preflight = await new InstallPreflight().ValidateAsync(plan, CancellationToken.None);
            Assert.True(preflight.IsValid);
            Assert.False(preflight.RequiresOverwriteConfirmation);

            var executor = new InstallExecutor(
                new ShellIntegrationService(),
                new CapturingUninstallRegistrationService(),
                new EnvironmentVariableIntegrationService(),
                sharedUserDataRoot: installDir,
                failureLogDirectory: Path.Combine(_root, "failure-logs"));
            var result = await executor.InstallAsync(plan, CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(result.InstallDirectory, "DeepVisionSharp.exe")));
            Assert.True(File.Exists(result.InstallationRecordPath));
        }
        finally
        {
            TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot);
        }
    }

    [Fact]
    public async Task Overwrite_install_on_shared_root_preserves_protected_user_data_including_readonly_files()
    {
        var (packagePath, installerExePath) = await BuildTestPackageAsync();
        var installDir = Path.Combine(_root, "shared-install");
        SeedProtectedUserData(installDir, markDataReadOnly: true);
        await File.WriteAllTextAsync(Path.Combine(installDir, "old-app-file.txt"), "stale-program");

        var package = await new PackageReader().OpenAsync(packagePath, CancellationToken.None);
        try
        {
            var plan = new InstallPlanner().CreatePlan(
                package,
                installDir,
                createStartMenuShortcut: false,
                createDesktopShortcut: false,
                installerExecutablePath: installerExePath,
                allowOverwriteExistingContents: true,
                registerEnvironmentVariables: false);

            var registration = new CapturingUninstallRegistrationService();
            var executor = new InstallExecutor(
                new ShellIntegrationService(),
                registration,
                new EnvironmentVariableIntegrationService(),
                sharedUserDataRoot: installDir,
                failureLogDirectory: Path.Combine(_root, "failure-logs"));
            await executor.InstallAsync(plan, CancellationToken.None);

            Assert.False(File.Exists(Path.Combine(installDir, "old-app-file.txt")));
            Assert.True(File.Exists(Path.Combine(installDir, "DeepVisionSharp.exe")));
            AssertProtectedUserDataIntact(installDir, expectDataReadOnly: true);

            // Estimated size must exclude the 1 MB file inside Data/.
            Assert.True(registration.LastEstimatedSizeBytes is > 0 and < 512 * 1024);
        }
        finally
        {
            ClearReadOnlyOnUserData(installDir);
            TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot);
        }
    }

    [Fact]
    public async Task Failed_install_on_shared_root_rolls_back_program_files_but_preserves_protected_user_data()
    {
        var (packagePath, installerExePath) = await BuildTestPackageAsync();
        var installDir = Path.Combine(_root, "rollback-shared");
        var failureLogDir = Path.Combine(_root, "failure-logs");
        SeedProtectedUserData(installDir, markDataReadOnly: true);

        var package = await new PackageReader().OpenAsync(packagePath, CancellationToken.None);
        try
        {
            var plan = new InstallPlanner().CreatePlan(
                package,
                installDir,
                createStartMenuShortcut: false,
                createDesktopShortcut: false,
                installerExecutablePath: installerExePath,
                allowOverwriteExistingContents: false,
                registerEnvironmentVariables: false);

            var executor = new InstallExecutor(
                new ShellIntegrationService(),
                new ThrowingUninstallRegistrationService(),
                new EnvironmentVariableIntegrationService(),
                sharedUserDataRoot: installDir,
                failureLogDirectory: failureLogDir);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                executor.InstallAsync(plan, CancellationToken.None));

            Assert.False(File.Exists(Path.Combine(installDir, "DeepVisionSharp.exe")));
            Assert.False(File.Exists(Path.Combine(installDir, "installation.json")));
            AssertProtectedUserDataIntact(installDir, expectDataReadOnly: true);
            Assert.NotEmpty(Directory.GetFiles(failureLogDir, "*.install-failed.log"));
        }
        finally
        {
            ClearReadOnlyOnUserData(installDir);
            TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot);
        }
    }

    [Fact]
    public async Task Failed_install_on_new_directory_deletes_newly_created_directory_on_rollback()
    {
        var (packagePath, installerExePath) = await BuildTestPackageAsync();
        var installDir = Path.Combine(_root, "rollback-brand-new");
        var failureLogDir = Path.Combine(_root, "failure-logs-new");

        var package = await new PackageReader().OpenAsync(packagePath, CancellationToken.None);
        try
        {
            var plan = new InstallPlanner().CreatePlan(
                package,
                installDir,
                createStartMenuShortcut: false,
                createDesktopShortcut: false,
                installerExecutablePath: installerExePath,
                allowOverwriteExistingContents: false,
                registerEnvironmentVariables: false);

            var executor = new InstallExecutor(
                new ShellIntegrationService(),
                new ThrowingUninstallRegistrationService(),
                new EnvironmentVariableIntegrationService(),
                sharedUserDataRoot: Path.Combine(_root, "other-shared-root"),
                failureLogDirectory: failureLogDir);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                executor.InstallAsync(plan, CancellationToken.None));

            Assert.False(Directory.Exists(installDir));
            Assert.NotEmpty(Directory.GetFiles(failureLogDir, "*.install-failed.log"));
        }
        finally
        {
            TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot);
        }
    }

    [Fact]
    public async Task Custom_non_shared_directory_containing_Data_folder_requires_overwrite_confirmation_and_uninstalls_completely()
    {
        var (packagePath, installerExePath) = await BuildTestPackageAsync();
        var customDir = Path.Combine(_root, "custom-non-shared");
        var otherSharedRoot = Path.Combine(_root, "actual-shared-root");
        Directory.CreateDirectory(Path.Combine(customDir, "Data"));
        await File.WriteAllTextAsync(Path.Combine(customDir, "Data", "unrelated.txt"), "custom");

        var package = await new PackageReader().OpenAsync(packagePath, CancellationToken.None);
        InstallResult installResult;
        try
        {
            var planNoOverwrite = new InstallPlanner().CreatePlan(
                package,
                customDir,
                createStartMenuShortcut: false,
                createDesktopShortcut: false,
                installerExecutablePath: installerExePath,
                allowOverwriteExistingContents: false,
                registerEnvironmentVariables: false);

            var preflight = await new InstallPreflight(otherSharedRoot).ValidateAsync(planNoOverwrite, CancellationToken.None);
            Assert.True(preflight.IsValid);
            Assert.True(preflight.RequiresOverwriteConfirmation);

            var planOverwrite = new InstallPlanner().CreatePlan(
                package,
                customDir,
                createStartMenuShortcut: false,
                createDesktopShortcut: false,
                installerExecutablePath: installerExePath,
                allowOverwriteExistingContents: true,
                registerEnvironmentVariables: false);

            var executor = new InstallExecutor(
                new ShellIntegrationService(),
                new CapturingUninstallRegistrationService(),
                new EnvironmentVariableIntegrationService(),
                sharedUserDataRoot: otherSharedRoot,
                failureLogDirectory: Path.Combine(_root, "failure-logs"));
            installResult = await executor.InstallAsync(planOverwrite, CancellationToken.None);
            Assert.False(Directory.Exists(Path.Combine(customDir, "Data")));
        }
        finally
        {
            TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot);
        }

        var uninstaller = new UninstallExecutor(
            new ShellIntegrationService(),
            new CapturingUninstallRegistrationService(),
            new EnvironmentVariableIntegrationService(),
            sharedUserDataRoot: otherSharedRoot);
        await uninstaller.UninstallAsync(installResult.InstallationRecordPath, CancellationToken.None);
        Assert.False(Directory.Exists(customDir));
    }

    [Fact]
    public async Task Uninstall_on_shared_root_removes_program_files_but_preserves_protected_user_data_including_readonly()
    {
        var (packagePath, installerExePath) = await BuildTestPackageAsync();
        var installDir = Path.Combine(_root, "uninstall-shared");
        SeedProtectedUserData(installDir, markDataReadOnly: true);

        var package = await new PackageReader().OpenAsync(packagePath, CancellationToken.None);
        InstallResult installResult;
        try
        {
            var plan = new InstallPlanner().CreatePlan(
                package,
                installDir,
                createStartMenuShortcut: false,
                createDesktopShortcut: false,
                installerExecutablePath: installerExePath,
                allowOverwriteExistingContents: true,
                registerEnvironmentVariables: false);

            var executor = new InstallExecutor(
                new ShellIntegrationService(),
                new CapturingUninstallRegistrationService(),
                new EnvironmentVariableIntegrationService(),
                sharedUserDataRoot: installDir,
                failureLogDirectory: Path.Combine(_root, "failure-logs"));
            installResult = await executor.InstallAsync(plan, CancellationToken.None);
        }
        finally
        {
            TempDirectoryCleanup.TryDeleteDirectory(package.ExtractedRoot);
        }

        try
        {
            var uninstaller = new UninstallExecutor(
                new ShellIntegrationService(),
                new CapturingUninstallRegistrationService(),
                new EnvironmentVariableIntegrationService(),
                sharedUserDataRoot: installDir);
            await uninstaller.UninstallAsync(installResult.InstallationRecordPath, CancellationToken.None);

            Assert.False(File.Exists(Path.Combine(installDir, "DeepVisionSharp.exe")));
            Assert.False(File.Exists(installResult.InstallationRecordPath));
            Assert.False(File.Exists(Path.Combine(installDir, "Uninstall.exe")));
            AssertProtectedUserDataIntact(installDir, expectDataReadOnly: true);
        }
        finally
        {
            ClearReadOnlyOnUserData(installDir);
        }
    }

    private async Task<(string PackagePath, string InstallerExePath)> BuildTestPackageAsync()
    {
        var pkgRoot = Path.Combine(_root, "pkg-" + Guid.NewGuid().ToString("N"));
        var payloadDir = Path.Combine(pkgRoot, "payload");
        var assetsDir = Path.Combine(pkgRoot, "assets");
        var versionDir = Path.Combine(pkgRoot, "version");
        var packagePath = Path.Combine(pkgRoot, "app.vrp");
        var installerExePath = Path.Combine(pkgRoot, "setup.exe");

        Directory.CreateDirectory(payloadDir);
        Directory.CreateDirectory(assetsDir);
        Directory.CreateDirectory(versionDir);

        await File.WriteAllTextAsync(Path.Combine(payloadDir, "DeepVisionSharp.exe"), "app-binary");
        await File.WriteAllTextAsync(Path.Combine(assetsDir, "license.txt"), "license");
        await File.WriteAllBytesAsync(Path.Combine(assetsDir, "logo.ico"), [0, 1, 2, 3]);
        await File.WriteAllTextAsync(Path.Combine(versionDir, "version.txt"), "0.0.1");
        await File.WriteAllTextAsync(installerExePath, "setup-host");

        var buildResult = await new RuntimePackageBuilder().BuildAsync(
            new PackagerOptions
            {
                RuntimeDirectory = payloadDir,
                AssetsDirectory = assetsDir,
                VersionDirectory = versionDir,
                RuntimeType = "app",
                PackageId = "vision.deepvisionsharp.test",
                ProductName = "DeepVisionSharp",
                Version = "0.0.1",
                EntryPoints =
                [
                    new EntryPointDescriptor("DeepVisionSharp", "DeepVisionSharp", "DeepVisionSharp.exe", string.Empty)
                ],
                EnvironmentVariables = [],
                OutputPackagePath = packagePath
            },
            progress: null,
            CancellationToken.None);

        Assert.True(buildResult.Success, buildResult.SummaryMessage);
        return (packagePath, installerExePath);
    }

    private static void SeedProtectedUserData(string installDir, bool markDataReadOnly = false)
    {
        Directory.CreateDirectory(Path.Combine(installDir, "Data"));
        Directory.CreateDirectory(Path.Combine(installDir, "Models", "pretrained"));
        Directory.CreateDirectory(Path.Combine(installDir, "Logs"));
        Directory.CreateDirectory(Path.Combine(installDir, "CrashDumps"));

        var dbPath = Path.Combine(installDir, "Data", "deepvision.db");
        var largeDatasetFile = Path.Combine(installDir, "Data", "dataset.bin");
        File.WriteAllText(dbPath, "sqlite-data");
        File.WriteAllBytes(largeDatasetFile, new byte[1024 * 1024]);
        File.WriteAllText(Path.Combine(installDir, "Models", "pretrained", "w.bin"), "weights");
        File.WriteAllText(Path.Combine(installDir, "Logs", "app.log"), "log-entry");
        File.WriteAllText(Path.Combine(installDir, "CrashDumps", "a.dmp"), "dump");

        if (markDataReadOnly)
        {
            File.SetAttributes(dbPath, File.GetAttributes(dbPath) | FileAttributes.ReadOnly);
        }
    }

    private static void ClearReadOnlyOnUserData(string installDir)
    {
        var dbPath = Path.Combine(installDir, "Data", "deepvision.db");
        if (File.Exists(dbPath))
        {
            File.SetAttributes(dbPath, File.GetAttributes(dbPath) & ~FileAttributes.ReadOnly);
        }
    }

    private static void AssertProtectedUserDataIntact(string installDir, bool expectDataReadOnly = false)
    {
        var dbPath = Path.Combine(installDir, "Data", "deepvision.db");
        Assert.Equal("sqlite-data", File.ReadAllText(dbPath));
        Assert.Equal("weights", File.ReadAllText(Path.Combine(installDir, "Models", "pretrained", "w.bin")));
        Assert.Equal("log-entry", File.ReadAllText(Path.Combine(installDir, "Logs", "app.log")));
        Assert.Equal("dump", File.ReadAllText(Path.Combine(installDir, "CrashDumps", "a.dmp")));
        if (expectDataReadOnly)
        {
            Assert.NotEqual(0, (int)(File.GetAttributes(dbPath) & FileAttributes.ReadOnly));
        }
    }

    private sealed class CapturingUninstallRegistrationService : UninstallRegistrationService
    {
        public long LastEstimatedSizeBytes { get; private set; }

        public override void WriteCurrentUserRegistration(
            string keyName,
            string displayName,
            string displayVersion,
            string publisher,
            string installDirectory,
            string displayIconPath,
            string uninstallCommand,
            string quietUninstallCommand,
            long estimatedSizeBytes)
        {
            LastEstimatedSizeBytes = estimatedSizeBytes;
        }

        public override void RemoveCurrentUserRegistration(string? keyName)
        {
        }
    }

    private sealed class ThrowingUninstallRegistrationService : UninstallRegistrationService
    {
        public override void WriteCurrentUserRegistration(
            string keyName,
            string displayName,
            string displayVersion,
            string publisher,
            string installDirectory,
            string displayIconPath,
            string uninstallCommand,
            string quietUninstallCommand,
            long estimatedSizeBytes) =>
            throw new IOException("Simulated registry/registration failure after payload extraction.");

        public override void RemoveCurrentUserRegistration(string? keyName)
        {
        }
    }
}
