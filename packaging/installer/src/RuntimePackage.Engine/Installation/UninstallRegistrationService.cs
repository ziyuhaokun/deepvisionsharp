using Microsoft.Win32;
using System.Runtime.Versioning;

namespace RuntimePackage.Engine.Installation;

[SupportedOSPlatform("windows")]
public class UninstallRegistrationService
{
    private const string RootPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    public virtual void WriteCurrentUserRegistration(
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
        using var key = Registry.CurrentUser.CreateSubKey(Path.Combine(RootPath, keyName).Replace('/', '\\'));
        key!.SetValue("DisplayName", displayName);
        key.SetValue("DisplayVersion", displayVersion);
        key.SetValue("Publisher", publisher);
        key.SetValue("InstallLocation", installDirectory);
        key.SetValue("DisplayIcon", displayIconPath);
        key.SetValue("UninstallString", uninstallCommand);
        key.SetValue("QuietUninstallString", quietUninstallCommand);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.UtcNow.ToString("yyyyMMdd"));

        var estimatedSizeKb = Math.Clamp(estimatedSizeBytes / 1024, 0, int.MaxValue);
        if (estimatedSizeKb > 0)
        {
            key.SetValue("EstimatedSize", (int)estimatedSizeKb, RegistryValueKind.DWord);
        }
    }

    internal virtual Action CaptureRollback(string keyName)
    {
        var path = Path.Combine(RootPath, keyName).Replace('/', '\\');
        using var key = Registry.CurrentUser.OpenSubKey(path);
        if (key is null)
        {
            return () => RemoveCurrentUserRegistration(keyName);
        }

        var values = key.GetValueNames().ToDictionary(name => name,
            name => (Value: key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!,
                Kind: key.GetValueKind(name)),
            StringComparer.OrdinalIgnoreCase);
        return () =>
        {
            using var restored = Registry.CurrentUser.CreateSubKey(path);
            foreach (var name in restored.GetValueNames())
            {
                if (!values.ContainsKey(name))
                {
                    restored.DeleteValue(name, throwOnMissingValue: false);
                }
            }

            foreach (var (name, entry) in values)
            {
                restored.SetValue(name, entry.Value, entry.Kind);
            }
        };
    }

    public virtual void RemoveCurrentUserRegistration(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return;
        }

        Registry.CurrentUser.DeleteSubKeyTree(Path.Combine(RootPath, keyName).Replace('/', '\\'), throwOnMissingSubKey: false);
    }
}
