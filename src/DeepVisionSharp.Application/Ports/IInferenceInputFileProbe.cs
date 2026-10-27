namespace DeepVisionSharp.Application.Ports;

public interface IInferenceInputFileProbe
{
    bool ModelExists(string? modelPath);

    bool ImageExists(string? imagePath);
}
