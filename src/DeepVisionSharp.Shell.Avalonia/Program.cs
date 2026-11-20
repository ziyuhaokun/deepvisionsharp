using Avalonia;

using DeepVisionSharp.Shell.Avalonia.Services;

namespace DeepVisionSharp.Shell.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            // 退出码在 Program 收口，不依赖 Avalonia 是否把 Exit 事件的 ApplicationExitCode
            // 回传给 lifetime 的返回值；优先级与 WPF Shell 一致：
            // lifetime 自己给了非零码就照用 → 验证 profile 记录过异常 70（WPF 在 OnExit 里这么写）
            // → 启动期失败 -1（WPF 用 Shutdown(-1)）→ 其余 0。
            if (exitCode != 0)
            {
                return exitCode;
            }

            if (App.ReportedExceptionCount > 0)
            {
                return 70;
            }

            return App.StartupFailed ? -1 : 0;
        }
        catch (Exception ex)
        {
            // Avalonia 平台自身起不来（缺 native 依赖、显示子系统初始化失败等），
            // Dispatcher 和窗口都还不存在，只能兜底：写日志 + 崩溃报告，交互模式再弹系统消息框。
            var promptUser = !StartupArguments.ContainsUiTest(args);
            UnhandledExceptionReporter.ReportStatic(ex, "Avalonia startup (pre-init)", promptUser);
            return -1;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
