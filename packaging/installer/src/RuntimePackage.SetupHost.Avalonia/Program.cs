using Avalonia;
using Avalonia.Controls;

namespace RuntimePackage.SetupHost;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
}
