using Microsoft.Extensions.DependencyInjection;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Infrastructure.Modules;
using DeepVisionSharp.Infrastructure.Services;
using Xunit;

namespace DeepVisionSharp.Infrastructure.Tests;

public sealed class InferenceModuleWiringTests
{
    [Fact]
    public void Inference_module_resolves_one_runtime_singleton_in_running_state()
    {
        var services = new ServiceCollection().AddInferenceModule();
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IInferenceRuntime>();
        var second = provider.GetRequiredService<IInferenceRuntime>();

        // Only the registration shape matters here: one instance, running. Which implementation it is
        // stays an implementation choice.
        Assert.Same(first, second);
        Assert.Equal(InferenceRuntimeState.Running, first.State);
    }

    [Fact]
    public void Inference_module_resolves_one_warmup_coordinator_singleton()
    {
        // The real runner needs the native backend graph (and logging); this test only checks the
        // coordinator's registration and lifetime.
        var services = new ServiceCollection().AddInferenceModule();
        services.AddSingleton<IInferenceRunner>(new NullRunner());
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IWarmupCoordinator>();
        var second = provider.GetRequiredService<IWarmupCoordinator>();

        Assert.Same(first, second);
        Assert.False(first.IsRunning);
        Assert.Null(first.DesiredKey);
    }

    private sealed class NullRunner : IInferenceRunner
    {
        public Task<InferenceResult> RunSingleAsync(
            string modelPath,
            string imagePath,
            InferenceConfig config,
            string modelType,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<InferenceWarmupResult> WarmupAsync(
            string modelPath,
            InferenceConfig config,
            string modelType,
            CancellationToken cancellationToken)
            => Task.FromResult(InferenceWarmupResult.Ok(0, 0, preflightCached: true, loadSkipped: true));

        public Task StopAsync(string? ownerKey, string? reason, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
