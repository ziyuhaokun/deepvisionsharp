using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Modules;

internal static class TrainingModule
{
    internal static IServiceCollection AddTrainingModule(this IServiceCollection services)
    {
        services.AddSingleton<TrainingProcessHost>();
        services.AddSingleton<ITrainingProcessHost>(sp => sp.GetRequiredService<TrainingProcessHost>());
        services.AddSingleton<TrainingJobStatusWriter>();
        services.AddSingleton<TrainingJobStartPreparer>();
        services.AddSingleton<TrainingBackendRegistry>();
        services.AddSingleton<TrainingEnvironmentCheckCoordinator>();
        services.AddSingleton<ITrainingBackend, TorchSharpClassificationTrainingBackend>();
        services.AddSingleton<ITrainingEnvironmentChecker, TorchSharpTrainingEnvironmentChecker>();
        services.AddSingleton<TrainingJobRecoveryCoordinator>();
        services.AddSingleton<TrainingRunQueue>();
        services.AddSingleton<ITrainingJobLifecycleService, TrainingJobLifecycleService>();
        services.AddSingleton<ITrainingWorkspacePlanner, TrainingWorkspacePlanner>();
        services.AddHostedService<TrainingJobRecoveryHostedService>();
        services.AddHostedService<TrainingJobReconcileHostedService>();
        services.AddHostedService<TrainingRunWorkerHostedService>();
        services.AddSingleton<ITrainingRunCleanupService, TrainingRunCleanupService>();
        return services;
    }
}
