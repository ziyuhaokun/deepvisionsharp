using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.TorchSharp.Classification;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>Bridges the Application port to the TorchSharp engine's bounded model cache.</summary>
internal sealed class InferenceModelCache : IInferenceModelCache
{
    private readonly TorchSharpClsEngine _engine;

    public InferenceModelCache(TorchSharpClsEngine engine)
    {
        _engine = engine;
    }

    public void Clear() => _engine.ClearCache();
}
