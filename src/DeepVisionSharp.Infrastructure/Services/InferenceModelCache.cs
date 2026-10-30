using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.TorchSharp.Classification;
using DeepVisionSharp.TorchSharp.Detection;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>Bridges the Application port to both TorchSharp engines' bounded model caches.</summary>
internal sealed class InferenceModelCache : IInferenceModelCache
{
    private readonly TorchSharpClsEngine _classification;
    private readonly TorchSharpDetectEngine _detection;

    public InferenceModelCache(TorchSharpClsEngine classification, TorchSharpDetectEngine detection)
    {
        _classification = classification;
        _detection = detection;
    }

    public void Clear()
    {
        _classification.ClearCache();
        _detection.ClearCache();
    }
}
