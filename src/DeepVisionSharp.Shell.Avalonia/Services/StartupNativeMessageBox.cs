using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 最后一道用户可见提示：Avalonia 的窗口与 Dispatcher 都不可用（平台初始化就失败）时，
/// 只剩系统消息框能让人看到话。正常的启动失败走 <see cref="StartupNoticeWindow"/>，不进这里。
/// </summary>
internal static class StartupNativeMessageBox
{
    private const uint MbOk = 0x00000000;
    private const uint MbIconError = 0x00000010;
    private const uint MbSetForeground = 0x00010000;
    private const uint MbTopMost = 0x00040000;

    public static void ShowError(string message)
    {
        try
        {
            // 阻塞式模态框是这一条路径唯一的选择，调用方必须已排除无人值守模式。
            _ = MessageBoxW(
                IntPtr.Zero,
                message,
                "DeepVisionSharp",
                MbOk | MbIconError | MbSetForeground | MbTopMost);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Trace.WriteLine($"Native message box unavailable: {ex.Message}. Message: {message}");
        }
    }

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr windowHandle, string text, string caption, uint type);
}
