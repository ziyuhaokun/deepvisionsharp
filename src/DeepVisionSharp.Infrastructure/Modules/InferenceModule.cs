using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Services;
using DeepVisionSharp.TorchSharp.Classification;

namespace DeepVisionSharp.Infrastructure.Modules;

internal static class InferenceModule
{
    internal static IServiceCollection AddInferenceModule(this IServiceCollection services)
    {
        services.AddSingleton<TorchSharpClsEngine>();
        services.AddSingleton<IInferenceRuntime>(provider =>
            new InferenceRuntime(new TorchSharpEngineControl(provider.GetRequiredService<TorchSharpClsEngine>())));
        services.AddSingleton<InferenceBackendRegistry>();
        services.AddSingleton<IInferenceBackend, TorchSharpClassificationInferenceBackend>();
        services.AddSingleton<IInferenceInputFileProbe, InferenceInputFileProbe>();
        services.AddSingleton<IInferenceRunner, InferenceRunner>();
        services.AddSingleton<IInferenceStatusReader, InferenceStatusReader>();
        services.AddSingleton<IInferenceShutdown>(provider => new InferenceShutdownService(
            provider.GetRequiredService<IInferenceRuntime>(),
            provider.GetRequiredService<TorchSharpClsEngine>()));
        services.AddSingleton<IInferenceModelCache, InferenceModelCache>();
        services.AddSingleton<IWarmupCoordinator, WarmupCoordinator>();
        return services;
    }
}
