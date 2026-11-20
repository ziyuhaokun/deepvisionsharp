using RuntimePackage.Engine.Installation;
using RuntimePackage.Engine.Packaging;
using RuntimePackage.Engine.Services;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace RuntimePackage.SetupHost;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime _desktop = null!;
    private string? _embeddedPackageRoot;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.Exit += OnExit;
            Dispatcher.UIThread.Post(() => Start(desktop.Args ?? []));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void Start(string[] args)
    {
        try
        {
            await StartAsync(args);
        }
        catch (Exception ex)
        {
            ShutdownWithMessage($"无法启动安装程序：{ex.Message}", args.Contains("--quiet", StringComparer.OrdinalIgnoreCase), 1);
        }
    }

    private async Task StartAsync(string[] args)
    {
        if (TryRunUninstallMode(args))
        {
            return;
        }

        var setupExecutablePath = ResolveExecutablePath();
        string? packagePath = null;

        if (TryParsePackageMode(args, out var parsedPackagePath))
        {
            packagePath = parsedPackagePath;
        }
        else
        {
            packagePath = ResolveSidecarPackagePath(setupExecutablePath);
        }

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            try
            {
                var payload = await new SetupBundleReader().ExtractPayloadAsync(setupExecutablePath, CancellationToken.None);
                _embeddedPackageRoot = payload.ExtractedRoot;
                packagePath = payload.ExtractedPackagePath;
            }
            catch
            {
                // Fall through to LoadBundledPackageAsync error UI when no package is available.
            }
        }

        var viewModel = OpenWizard();

        if (!string.IsNullOrWhiteSpace(packagePath))
        {
            await viewModel.LoadPackageFileAsync(packagePath, setupExecutablePath, CancellationToken.None);
        }
        else
        {
            await viewModel.LoadBundledPackageAsync(setupExecutablePath, CancellationToken.None);
        }
    }

    private void OnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_embeddedPackageRoot is not null)
        {
            TempDirectoryCleanup.TryDeleteDirectory(_embeddedPackageRoot);
        }
    }

    private SetupViewModel OpenWizard()
    {
        var window = new SetupShellWindow();
        _desktop.MainWindow = window;
        window.Show();
        return window.ViewModel;
    }

    private bool TryRunUninstallMode(string[] args)
    {
        if (args.Length == 0)
        {
            return TryRunUninstallModeFromInstalledUninstaller();
        }

        if (!string.Equals(args[0], "--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var quiet = args.Any(arg => string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase));
        var installationRecordPath = args
            .Skip(1)
            .FirstOrDefault(arg => !string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(installationRecordPath))
        {
            ShutdownWithMessage("缺少安装记录路径。", quiet, exitCode: 2);
            return true;
        }

        if (TryRelaunchInstalledUninstallerFromTemp(installationRecordPath, quiet))
        {
            return true;
        }

        if (quiet)
        {
            _ = RunUninstallAndShutdownAsync(installationRecordPath, quiet);
            return true;
        }

        _ = ShowUninstallWizardAsync(installationRecordPath);
        return true;
    }

    private bool TryRunUninstallModeFromInstalledUninstaller()
    {
        var executablePath = ResolveExecutablePath();
        if (!string.Equals(Path.GetFileName(executablePath), "Uninstall.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var installationRecordPath = Path.Combine(
            Path.GetDirectoryName(executablePath) ?? string.Empty,
            "installation.json");

        if (!File.Exists(installationRecordPath))
        {
            ShutdownWithMessage("缺少安装记录路径。", quiet: false, exitCode: 2);
            return true;
        }

        if (TryRelaunchInstalledUninstallerFromTemp(installationRecordPath, quiet: false))
        {
            return true;
        }

        _ = ShowUninstallWizardAsync(installationRecordPath);
        return true;
    }

    private bool TryRelaunchInstalledUninstallerFromTemp(string installationRecordPath, bool quiet)
    {
        var executablePath = ResolveExecutablePath();
        if (!string.Equals(Path.GetFileName(executablePath), "Uninstall.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var installDirectory = Path.GetDirectoryName(Path.GetFullPath(installationRecordPath));
        if (string.IsNullOrWhiteSpace(installDirectory) ||
            !IsSameOrUnder(Path.GetFullPath(executablePath), installDirectory))
        {
            return false;
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "RuntimePackage.Uninstall",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var tempExecutablePath = Path.Combine(tempDirectory, "RuntimePackage.Uninstaller.exe");
        File.Copy(executablePath, tempExecutablePath, overwrite: true);

        var startInfo = new ProcessStartInfo
        {
            FileName = tempExecutablePath,
            WorkingDirectory = tempDirectory,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("--uninstall");
        startInfo.ArgumentList.Add(installationRecordPath);
        if (quiet)
        {
            startInfo.ArgumentList.Add("--quiet");
        }

        Process.Start(startInfo);
        _desktop.Shutdown(0);
        return true;
    }

    private async Task RunUninstallAndShutdownAsync(string installationRecordPath, bool quiet)
    {
        try
        {
            await new UninstallExecutor().UninstallAsync(installationRecordPath, CancellationToken.None);
            ShutdownWithMessage("卸载完成。", quiet, exitCode: 0);
        }
        catch (Exception ex)
        {
            ShutdownWithMessage($"卸载失败：{ex.Message}", quiet, exitCode: 1);
        }
    }

    private async Task ShowUninstallWizardAsync(string installationRecordPath)
    {
        var viewModel = OpenWizard();
        await viewModel.LoadUninstallRecordAsync(installationRecordPath, CancellationToken.None);
    }

    private void ShutdownWithMessage(string message, bool quiet, int exitCode)
    {
        if (quiet)
        {
            _desktop.Shutdown(exitCode);
            return;
        }

        var notice = new SetupNoticeWindow("安装程序", message);
        _desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnExplicitShutdown;
        notice.Closed += (_, _) => _desktop.Shutdown(exitCode);
        notice.Show();
    }

    private static string ResolveExecutablePath() =>
        Environment.ProcessPath ?? throw new InvalidOperationException("无法解析安装器可执行文件路径。");

    private static string? ResolveSidecarPackagePath(string setupExecutablePath)
    {
        var sidecarPath = Path.ChangeExtension(setupExecutablePath, ".vrp");
        return File.Exists(sidecarPath) ? sidecarPath : null;
    }

    private static bool IsSameOrUnder(string candidate, string parent)
    {
        var normalizedCandidate = Path.GetFullPath(candidate);
        var normalizedParent = Path.GetFullPath(parent);
        if (string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var parentPrefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(parentPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParsePackageMode(string[] args, out string packagePath)
    {
        packagePath = string.Empty;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, "--package", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                packagePath = args[++i];
            }
        }

        return !string.IsNullOrWhiteSpace(packagePath);
    }
}
