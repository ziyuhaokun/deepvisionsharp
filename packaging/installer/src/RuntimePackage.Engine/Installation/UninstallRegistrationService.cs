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

    public virtual void RemoveCurrentUserRegistration(string? keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
        {
            return;
        }

        Registry.CurrentUser.DeleteSubKeyTree(Path.Combine(RootPath, keyName).Replace('/', '\\'), throwOnMissingSubKey: false);
    }
}
