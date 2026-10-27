using System.IO;
using System.Runtime.InteropServices;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// 高性能零拷贝文件链接器。
/// 在 Windows NTFS/ReFS 分区优先使用 Win32 CreateHardLinkW 创建硬链接（无需管理员权限），
/// 跨驱动器或环境不支持时自动透明回退至标准 File.Copy。
/// </summary>
internal static class FastFileLinker
{
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, nint lpSecurityAttributes);

    public static bool TryLinkOrCopy(string sourcePath, string destinationPath, bool overwrite = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (overwrite && File.Exists(destinationPath))
        {
            try
            {
                File.Delete(destinationPath);
            }
            catch
            {
                File.Copy(sourcePath, destinationPath, overwrite: true);
                return false;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (CreateHardLink(destinationPath, sourcePath, nint.Zero))
                {
                    return true;
                }
            }
            catch
            {
                // Fallback to copy
            }
        }
        else
        {
            try
            {
                File.CreateSymbolicLink(destinationPath, sourcePath);
                return true;
            }
            catch
            {
                // Fallback to copy
            }
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
        return false;
    }
}
