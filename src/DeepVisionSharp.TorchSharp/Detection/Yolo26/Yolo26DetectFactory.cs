using TorchSharp;

namespace DeepVisionSharp.TorchSharp.Detection.Yolo26;

/// <summary>Product detection keys and their TorchSharp construction.</summary>
public static class Yolo26DetectFactory
{
    public const string ArchN = "yolo26n-det";
    public const string ArchS = "yolo26s-det";
    public const string ArchM = "yolo26m-det";
    public const string ArchL = "yolo26l-det";
    public const string ArchX = "yolo26x-det";

    public static string ResolveArch(string? archName)
    {
        if (string.IsNullOrWhiteSpace(archName))
        {
            return ArchN;
        }

        var normalized = archName.Trim().ToLowerInvariant().Replace('_', '-');
        return normalized switch
        {
            "yolo26n" or "yolo26-n" or "yolo26-n-det" => ArchN,
            "yolo26s" or "yolo26-s" or "yolo26-s-det" => ArchS,
            "yolo26m-det" => ArchM,
            "yolo26l" or "yolo26-l" or "yolo26-l-det" => ArchL,
            "yolo26x" or "yolo26-x" or "yolo26-x-det" => ArchX,
            _ => ArchN
        };
    }

    public static Yolo26DetectModel Create(string? archName, int inputChannels, int numClasses) =>
        Yolo26DetectModel.Create(ResolveArch(archName), inputChannels, numClasses);
}
