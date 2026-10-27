using DeepVisionSharp.Shell.Views.Pages;
using Wpf.Ui;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Shell.Services.Navigation;

public sealed class AppNavigationService : IAppNavigationService
{
    private readonly INavigationService _navigation;
    private readonly WorkspaceSessionService _workspace;

    public AppNavigationService(INavigationService navigation, WorkspaceSessionService workspace)
    {
        _navigation = navigation;
        _workspace = workspace;
    }

    public void Navigate(AppRoute route)
    {
        if (route is not (AppRoute.Dashboard or AppRoute.Settings)
            && _workspace.Current.Project is { } project
            && !ProjectCapabilityCatalog.IsActive(project.Type))
        {
            _workspace.Clear();
            route = AppRoute.Dashboard;
        }
        _navigation.Navigate(MapPageType(route));
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

