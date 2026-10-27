using Microsoft.Extensions.DependencyInjection;

namespace DeepVisionSharp.Shell.Services.Labeling.WriteBack;

public static class LabelingWriteBackDependencyInjection
{
    /// <summary>
    /// Registers inference write-back strategies and the kind→strategy registry.
    /// Strategies are singleton (stateless); the registry indexes them by write-back kind.
    /// </summary>
    public static IServiceCollection AddLabelingInferenceWriteBack(this IServiceCollection services)
    {
        services.AddSingleton<ILabelingInferenceWriteBackStrategy, ImageClassTop1WriteBackStrategy>();
        services.AddSingleton<LabelingInferenceWriteBackRegistry>();
        return services;
    }
}
