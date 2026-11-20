using System.Diagnostics;
using System.IO;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 用系统默认程序打开文件夹，与 WPF Shell 的 <c>ShellProcessLauncher</c> 同语义
/// （路径不存在或启动失败就返回 false，不抛）。两个 Shell 互不引用，所以各留一份。
/// </summary>
public sealed class AvaloniaProcessLauncher : IProcessLauncher
{
    public bool TryOpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = folderPath,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
