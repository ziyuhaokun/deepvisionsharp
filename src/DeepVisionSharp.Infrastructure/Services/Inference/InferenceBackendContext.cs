using DeepVisionSharp.Application.Inference;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed record InferenceBackendContext(
    string ModelPath,
    string ImagePath,
    InferenceConfig Config,
    string ModelType);
