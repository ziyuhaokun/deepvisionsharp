using RuntimePackage.Engine.Models;
using System.Runtime.Versioning;
using System.Text.Json;

namespace RuntimePackage.Engine.Installation;

[SupportedOSPlatform("windows")]
public sealed class UninstallExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ShellIntegrationService _shellIntegration;
    private readonly UninstallRegistrationService _uninstallRegistration;
    private readonly EnvironmentVariableIntegrationService _environmentVariables;

    public UninstallExecutor()
        : this(new ShellIntegrationService(), new UninstallRegistrationService(), new EnvironmentVariableIntegrationService())
    {
    }

    internal UninstallExecutor(
        ShellIntegrationService shellIntegration,
        UninstallRegistrationService uninstallRegistration)
        : this(shellIntegration, uninstallRegistration, new EnvironmentVariableIntegrationService())
    {
    }

    internal UninstallExecutor(
        ShellIntegrationService shellIntegration,
        UninstallRegistrationService uninstallRegistration,
        EnvironmentVariableIntegrationService environmentVariables)
    {
        _shellIntegration = shellIntegration;
        _uninstallRegistration = uninstallRegistration;
        _environmentVariables = environmentVariables;
    }

    public async Task UninstallAsync(string installationRecordPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installationRecordPath);
        cancellationToken.ThrowIfCancellationRequested();

        var installationJson = await File.ReadAllTextAsync(installationRecordPath, cancellationToken);
        var record = JsonSerializer.Deserialize<InstallationRecord>(installationJson, JsonOptions)
            ?? throw new InvalidOperationException("无法读取安装记录。");

        var canonicalRecordPath = NormalizePath(installationRecordPath);
        var canonicalInstallDirectory = NormalizePath(record.InstallDirectory);
        var expectedRecordPath = NormalizePath(Path.Combine(canonicalInstallDirectory, "installation.json"));

        if (!string.Equals(canonicalRecordPath, expectedRecordPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("安装记录路径与安装目录不匹配。");
        }

        List<string> errors = [];

        TryUninstallStep(errors, "移除快捷方式", () =>
            _shellIntegration.RemoveShortcuts(record.StartMenuShortcutPath, record.DesktopShortcutPath));

        TryUninstallStep(errors, "移除卸载注册", () =>
            _uninstallRegistration.RemoveCurrentUserRegistration(record.UninstallRegistryKeyName));

        TryUninstallStep(errors, "恢复环境变量", () =>
            _environmentVariables.RestoreEnvironmentVariables(record.EnvironmentVariables));

        TryUninstallStep(errors, "删除安装目录", () =>
        {
            if (Directory.Exists(canonicalInstallDirectory))
            {
                ClearReadOnlyAttributes(canonicalInstallDirectory);
                DeleteInstallDirectory(canonicalInstallDirectory);
            }
        });

        if (errors.Count > 0)
        {
            throw new AggregateException(
                $"卸载过程中有 {errors.Count} 个步骤失败：{string.Join("; ", errors)}",
                errors.Select(e => new InvalidOperationException(e)));
        }
    }

    private static void TryUninstallStep(List<string> errors, string stepName, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            errors.Add($"{stepName}: {ex.Message}");
        }
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path);

    private static void DeleteInstallDirectory(string installDirectory)
    {
        var currentProcessPath = Environment.ProcessPath;
        var protectedPaths = string.IsNullOrWhiteSpace(currentProcessPath)
            ? []
            : new HashSet<string>([NormalizePath(currentProcessPath)], StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(installDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            var normalizedFile = NormalizePath(file);
            if (protectedPaths.Contains(normalizedFile))
            {
                continue;
            }

            ClearReadOnlyAttribute(file);
            File.Delete(file);
        }

        foreach (var directory in Directory.EnumerateDirectories(installDirectory, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }

        if (!Directory.EnumerateFileSystemEntries(installDirectory).Any())
        {
            Directory.Delete(installDirectory);
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
}
