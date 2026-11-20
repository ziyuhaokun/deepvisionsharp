using DeepVisionSharp.Presentation.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DeepVisionSharp.Shell.Avalonia.Composition;

/// <summary>
/// 设计时/无容器宿主（XAML 设计器、无头渲染直接 <c>new MainWindow()</c>）用真实启动参数文件
/// 建一个容器，让页面能拿到真 ViewModel 与真数据。
/// <para>
/// 建不起来（数据目录不可用、业务初始化抛错）就把 null 交回去：页面退回 stub 上下文，
/// 外壳仍然渲染得出来，失败原因写进 Trace。同一进程只尝试一次。
/// </para>
/// </summary>
internal static class DesignTimeComposition
{
    private static IServiceProvider? _services;
    private static bool _attempted;

    public static IServiceProvider? TryCreateServices()
    {
        if (_attempted)
        {
            return _services;
        }

        _attempted = true;
        try
        {
            _services = AppComposition.Create(DeepVisionLaunchOptions.Default).Services;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Design-time composition unavailable, falling back to stub data: {ex}");
            _services = null;
        }

        return _services;
    }
}
