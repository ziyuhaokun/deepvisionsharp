using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Services;

namespace DeepVisionSharp.Infrastructure.Modules;

internal static class ModelArtifactsModule
{
    internal static IServiceCollection AddModelArtifactsModule(this IServiceCollection services)
    {
        services.AddSingleton<ModelMetadataService>();
        services.AddSingleton<IModelMetadataReader, ModelMetadataReader>();
        services.AddSingleton<IModelCheckpointFileService, ModelCheckpointFileService>();
        services.AddSingleton<ModelArtifactHandlerRegistry>();
        services.AddSingleton<IModelArtifactHandler, ClassificationModelArtifactHandler>();
        services.AddSingleton<IModelArtifactHandler, DetectionModelArtifactHandler>();
        services.AddSingleton<ITrainedModelArtifactService, TrainedModelArtifactService>();
        return services;
    }
}
