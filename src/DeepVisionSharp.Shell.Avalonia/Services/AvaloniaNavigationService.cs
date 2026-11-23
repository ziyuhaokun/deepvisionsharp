using Avalonia.Controls;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Navigation;
using DeepVisionSharp.Shell.Avalonia.Services.Hosting;
using DeepVisionSharp.Shell.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DeepVisionSharp.Shell.Avalonia.Services;

/// <summary>
/// 路由 → 页面。每个路由的页面从容器解析并按路由缓存（切回同一导航项不重建）；
/// 容器里没有该页面（未注册）时返回 null，由主窗口画占位页。
/// <para>
/// 容器可能不存在（设计时 / 无头渲染直接 <c>new MainWindow()</c>）：此时页面自己退回 stub 上下文。
/// </para>
/// </summary>
public sealed class AvaloniaNavigationService : IAppNavigationService
{
    private readonly IServiceProvider? _services;
    private readonly WorkspaceSessionService? _workspace;
    private readonly Dictionary<AppRoute, Control> _pages = new();
    private IPageHost? _pageHost;

    public AvaloniaNavigationService(IServiceProvider? services, WorkspaceSessionService? workspace)
    {
        _services = services;
        _workspace = workspace;
    }

    /// <summary>导航完成（页面已放进内容区）后触发，宿主据此同步侧边导航的选中态。</summary>
    public event EventHandler<AppRoute>? Navigated;

    /// <summary>
    /// 由 <c>MainWindow</c> 在 <c>InitializeComponent()</c> 之后调用。
    /// 宿主不进构造函数：窗口本身就在 DI 图里，构造期互相持有会撞循环依赖。
    /// </summary>
    public void Attach(IPageHost pageHost)
    {
        ArgumentNullException.ThrowIfNull(pageHost);
        _pageHost = pageHost;
    }

    public void Navigate(AppRoute route)
    {
        if (_workspace is not null
            && route is not (AppRoute.Dashboard or AppRoute.Settings)
            && _workspace.Current.Project is { } project
            && !ProjectCapabilityCatalog.IsActive(project.Type))
        {
            _workspace.Clear();
            route = AppRoute.Dashboard;
        }

        var pageHost = _pageHost
            ?? throw new InvalidOperationException("导航宿主未挂载");

        if (!_pages.TryGetValue(route, out var cached))
        {
            cached = CreatePage(route);
            if (cached is not null)
            {
                _pages.Add(route, cached);
            }
        }

        pageHost.Show(cached, route);

        Navigated?.Invoke(this, route);
    }

    private Control? CreatePage(AppRoute route) => route switch
    {
        AppRoute.Dashboard => _services?.GetService(typeof(DashboardPage)) as DashboardPage ?? new DashboardPage(),
        AppRoute.Gallery => _services?.GetService(typeof(GalleryPage)) as GalleryPage ?? new GalleryPage(),
        AppRoute.Labeling => _services?.GetService(typeof(LabelingPage)) as LabelingPage ?? new LabelingPage(),
        AppRoute.Models => _services?.GetService(typeof(ModelsPage)) as ModelsPage ?? new ModelsPage(),
        AppRoute.Training => _services?.GetService(typeof(TrainingPage)) as TrainingPage ?? new TrainingPage(),
        AppRoute.Inference => _services?.GetService(typeof(InferencePage)) as InferencePage ?? new InferencePage(),
        AppRoute.Settings => _services?.GetService(typeof(SettingsPage)) as SettingsPage ?? new SettingsPage(),
        _ => null,
    };
}
