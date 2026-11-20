using System.Diagnostics;
using System.IO;

using DeepVisionSharp.Presentation.Services;

using Microsoft.Win32;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 为当前进程打开 Windows 错误报告 LocalDumps（HKCU，不需要提权）。
/// 托管异常拦截不到的原生/硬崩溃也能在 <see cref="ShellRuntimePaths.CrashDumpDirectory"/> 留 minidump。
/// </summary>
internal static class StartupCrashDumpRegistration
{
    /// <summary>WER DumpType：1 = Mini dump。</summary>
    private const int MiniDumpType = 1;

    private const int DefaultDumpCount = 5;

    public static StartupCrashDumpRegistrationResult EnsureEnabled() => Register(ShellRuntimePaths.Current.CrashDumpDirectory);

    /// <summary>UI 测试/无人值守模式不写注册表（不能让测试进程留下机器级副作用）。</summary>
    public static StartupCrashDumpRegistrationResult Skipped(string reason) =>
        StartupCrashDumpRegistrationResult.Failed(ResolveExeFileName(), ShellRuntimePaths.Current.CrashDumpDirectory, reason);

    private static StartupCrashDumpRegistrationResult Register(string dumpFolder)
    {
        var exeName = ResolveExeFileName();
        try
        {
            Directory.CreateDirectory(dumpFolder);

            var keyPath = $@"Software\Microsoft\Windows\Windows Error Reporting\LocalDumps\{exeName}";

            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            if (key is null)
            {
                return StartupCrashDumpRegistrationResult.Failed(exeName, dumpFolder, "无法创建 LocalDumps 注册表项。");
            }

            key.SetValue("DumpFolder", dumpFolder, RegistryValueKind.ExpandString);
            key.SetValue("DumpCount", DefaultDumpCount, RegistryValueKind.DWord);
            key.SetValue("DumpType", MiniDumpType, RegistryValueKind.DWord);

            return StartupCrashDumpRegistrationResult.Succeeded(exeName, dumpFolder);
        }
        catch (Exception ex)
        {
            return StartupCrashDumpRegistrationResult.Failed(exeName, dumpFolder, ex.Message);
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
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // 某些宿主形态下 MainModule 读不到，退回 Environment.ProcessPath。
        }

        var path = Environment.ProcessPath;
        return !string.IsNullOrWhiteSpace(path) ? Path.GetFileName(path) : "DeepVisionSharp.Avalonia.exe";
    }
}

internal sealed record StartupCrashDumpRegistrationResult(
    bool Success,
    string ExeFileName,
    string DumpFolder,
    string? Error)
{
    public static StartupCrashDumpRegistrationResult Succeeded(string exeFileName, string dumpFolder) =>
        new(true, exeFileName, dumpFolder, null);

    public static StartupCrashDumpRegistrationResult Failed(string exeFileName, string dumpFolder, string error) =>
        new(false, exeFileName, dumpFolder, error);
}
