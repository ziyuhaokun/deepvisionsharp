using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Shell.ViewModels.Training;
using DeepVisionSharp.Shell.Views.Pages;

namespace DeepVisionSharp.Shell.Services.Training;

internal static class TrainingWorkspaceRegistration
{
    internal static IServiceCollection AddTrainingWorkspace(this IServiceCollection services)
    {
        services.AddTransient<TrainingMetricsPoller>();
        services.AddTransient<TrainingPreviewPoller>();
        services.AddTransient<TrainingResourceMonitor>();
        services.AddTransient<TrainingResourcePoller>();
        services.AddTransient<TrainingWorkspaceCoordinator>();
        services.AddTransient<TrainingViewModel>();
        services.AddTransient<TrainingPage>();
        return services;
    }
}
