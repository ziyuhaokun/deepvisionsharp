using System.Diagnostics;
using System.IO;
using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Shell.Services;

public sealed class ShellProcessLauncher : IProcessLauncher
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
