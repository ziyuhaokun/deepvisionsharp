using DeepVisionSharp.Application.Ports;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class InferenceInputFileProbe : IInferenceInputFileProbe
{
    public bool ModelExists(string? modelPath) =>
        !string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath);

    public bool ImageExists(string? imagePath) =>
        !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath);
}
