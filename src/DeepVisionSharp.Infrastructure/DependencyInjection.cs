using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Infrastructure.Modules;

namespace DeepVisionSharp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddDeepVisionInfrastructure(this IServiceCollection services)
    {
        services.AddDataModule();
        services.AddModelArtifactsModule();
        services.AddTrainingModule();
        services.AddInferenceModule();
        return services;
    }
}
