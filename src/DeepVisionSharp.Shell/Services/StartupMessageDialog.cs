using DeepVisionSharp.Shell.Views.Dialogs;

namespace DeepVisionSharp.Shell.Services;

public static class StartupMessageDialog
{
    public const string ProductDisplayName = "DeepVisionSharp";

    public static void Show(
        string message,
        string title = ProductDisplayName,
        StartupMessageKind kind = StartupMessageKind.Information)
    {
        var dialog = new StartupMessageWindow(title, message, kind)
        {
            // Ensure second-instance notice is visible even if main window is focused.
            Topmost = true
        };
        dialog.ShowDialog();
    }

    public static void ShowAlreadyRunning()
    {
        Show(
            "检测到已有运行中的实例。请切换到已打开的窗口继续使用，无需再次启动。",
            title: ProductDisplayName,
            kind: StartupMessageKind.AlreadyRunning);
    }
}
