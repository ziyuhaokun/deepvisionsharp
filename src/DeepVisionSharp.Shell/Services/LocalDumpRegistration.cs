using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DeepVisionSharp.Shell.Services;

/// <summary>
/// Enables Windows Error Reporting LocalDumps for this process (HKCU, no elevation).
/// Hard crashes that bypass managed handlers can still leave a minidump for diagnosis.
/// </summary>
public static class LocalDumpRegistration
{
    public const int DefaultDumpCount = 5;

    /// <summary>WER DumpType: 1 = Mini dump.</summary>
    public const int MiniDumpType = 1;

    public static string CrashDumpDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DeepVisionSharp",
        "CrashDumps");

    public static LocalDumpRegistrationResult EnsureEnabled()
    {
        try
        {
            Directory.CreateDirectory(CrashDumpDirectory);

            var exeName = ResolveExeFileName();
            var keyPath = $@"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\{exeName}";

            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            if (key is null)
            {
                return LocalDumpRegistrationResult.Failed(
                    exeName,
                    CrashDumpDirectory,
                    "无法创建 LocalDumps 注册表项。");
            }

            key.SetValue("DumpFolder", CrashDumpDirectory, RegistryValueKind.ExpandString);
            key.SetValue("DumpCount", DefaultDumpCount, RegistryValueKind.DWord);
            key.SetValue("DumpType", MiniDumpType, RegistryValueKind.DWord);

            return LocalDumpRegistrationResult.Succeeded(exeName, CrashDumpDirectory);
        }
        catch (Exception ex)
        {
            return LocalDumpRegistrationResult.Failed(
                ResolveExeFileName(),
                CrashDumpDirectory,
                ex.Message);
        }
    }

    private static string ResolveExeFileName()
    {
        try
        {
            var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
            if (!string.IsNullOrWhiteSpace(moduleName))
            {
                return moduleName;
            }
        }
        catch
        {
            // MainModule can throw under some host scenarios.
        }

        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.GetFileName(path);
        }

        return "DeepVisionSharp.exe";
    }
}

public sealed record LocalDumpRegistrationResult(
    bool Success,
    string ExeFileName,
    string DumpFolder,
    string? Error)
{
    public static LocalDumpRegistrationResult Succeeded(string exeFileName, string dumpFolder) =>
        new(true, exeFileName, dumpFolder, null);

    public static LocalDumpRegistrationResult Failed(string exeFileName, string dumpFolder, string error) =>
        new(false, exeFileName, dumpFolder, error);
}
