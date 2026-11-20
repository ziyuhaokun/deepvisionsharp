using DeepVisionSharp.Shell.Services.Hosting;
using DeepVisionSharp.Shell.Views.Pages;
using DeepVisionSharp.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace DeepVisionSharp.Shell.Services.Navigation;

public sealed class AppNavigationService : IAppNavigationService
{
    private readonly IServiceProvider _services;
    private readonly WorkspaceSessionService _workspace;
    private IPageHost? _pageHost;

    public AppNavigationService(IServiceProvider services, WorkspaceSessionService workspace)
    {
        _services = services;
        _workspace = workspace;
    }

    /// <summary>
    /// 由 <c>MainWindow</c> 在 <c>InitializeComponent()</c> 之后调用。
    /// 宿主不进构造函数：MainWindow 本身就在 DI 图里，构造期互相持有会撞循环依赖。
    /// </summary>
    public void Attach(IPageHost pageHost)
    {
        ArgumentNullException.ThrowIfNull(pageHost);
        _pageHost = pageHost;
    }

    /// <summary>导航完成（页面已放进内容区）后触发，宿主据此同步侧边导航的选中态。</summary>
    public event EventHandler<AppRoute>? Navigated;

    public void Navigate(AppRoute route)
    {
        if (route is not (AppRoute.Dashboard or AppRoute.Settings)
            && _workspace.Current.Project is { } project
            && !ProjectCapabilityCatalog.IsActive(project.Type))
        {
            _workspace.Clear();
            route = AppRoute.Dashboard;
        }

        var pageHost = _pageHost
            ?? throw new InvalidOperationException("导航宿主未挂载");

        pageHost.Show(_services.GetRequiredService(MapPageType(route)));
        Navigated?.Invoke(this, route);
    }

    private static Type MapPageType(AppRoute route)
        => route switch
        {
            AppRoute.Dashboard => typeof(DashboardPage),
            AppRoute.Gallery => typeof(GalleryPage),
            AppRoute.Labeling => typeof(LabelingPage),
            AppRoute.Models => typeof(ModelsPage),
            AppRoute.Training => typeof(TrainingPage),
            AppRoute.Inference => typeof(InferencePage),
            AppRoute.Settings => typeof(SettingsPage),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown app route.")
        };
}
