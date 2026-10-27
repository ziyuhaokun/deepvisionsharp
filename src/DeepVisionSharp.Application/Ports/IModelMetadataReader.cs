namespace DeepVisionSharp.Application.Ports;

public interface IModelMetadataReader
{
    ModelDisplayMetrics? Read(string checkpointPath);
}

public sealed record ModelDisplayMetrics(
    int Epochs,
    double BestMap,
    double BestMap50,
    bool HasMetadata,
    int? InputSize = null,
    int InputChannels = 3);
