using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Shell.Views.Pages;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Training;
using DeepVisionSharp.Presentation.ViewModels.Training;

namespace DeepVisionSharp.Shell.Services.Training;

internal static class TrainingWorkspaceRegistration
{
    internal static IServiceCollection AddTrainingWorkspace(this IServiceCollection services)
    {
        services.AddTransient<TrainingMetricsPoller>();
        services.AddTransient<TrainingPreviewPoller>();
        services.AddTransient<TrainingResourceMonitor>();
        services.AddTransient<ISystemResourceMonitor>(sp => sp.GetRequiredService<TrainingResourceMonitor>());
        services.AddTransient<TrainingResourcePoller>();
        services.AddTransient<TrainingWorkspaceCoordinator>();
        services.AddTransient<TrainingViewModel>();
        services.AddTransient<TrainingPage>();
        return services;
    }
}
