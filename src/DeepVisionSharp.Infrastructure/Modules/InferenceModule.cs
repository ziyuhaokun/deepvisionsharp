using Microsoft.Extensions.DependencyInjection;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Services;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Detection;

namespace DeepVisionSharp.Infrastructure.Modules;

internal static class InferenceModule
{
    internal static IServiceCollection AddInferenceModule(this IServiceCollection services)
    {
        services.AddSingleton<TorchSharpClsEngine>();
        services.AddSingleton<TorchSharpDetectEngine>();
        services.AddSingleton<IInferenceRuntime>(provider => new InferenceRuntime(
            new CompositeTorchSharpEngineControl(
                provider.GetRequiredService<TorchSharpClsEngine>(),
                provider.GetRequiredService<TorchSharpDetectEngine>())));
        services.AddSingleton<InferenceBackendRegistry>();
        services.AddSingleton<IInferenceBackend, TorchSharpClassificationInferenceBackend>();
        services.AddSingleton<IInferenceBackend, TorchSharpDetectionInferenceBackend>();
        services.AddSingleton<IInferenceInputFileProbe, InferenceInputFileProbe>();
        services.AddSingleton<IInferenceRunner, InferenceRunner>();
        services.AddSingleton<IInferenceStatusReader, InferenceStatusReader>();
        services.AddSingleton<IInferenceShutdown>(provider => new InferenceShutdownService(
            provider.GetRequiredService<IInferenceRuntime>(),
            new CompositeInferenceEngine(
                provider.GetRequiredService<TorchSharpClsEngine>(),
                provider.GetRequiredService<TorchSharpDetectEngine>())));
        services.AddSingleton<IInferenceModelCache, InferenceModelCache>();
        services.AddSingleton<IWarmupCoordinator, WarmupCoordinator>();
        return services;
    }
}
