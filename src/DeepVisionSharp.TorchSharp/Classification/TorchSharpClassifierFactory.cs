using TorchSharp;
using DeepVisionSharp.TorchSharp.Classification.ResNet;

namespace DeepVisionSharp.TorchSharp.Classification;

public static class TorchSharpClassifierFactory
{
    // torchvision-compatible: stride-2 stem + maxpool and torchvision module names, so the
    // ImageNet state dict applies without key mapping.
    public const string ArchResNetTiny = "resnet_tiny_v2";
    public const string ArchResNet18 = "resnet18_v2";
    public const string ArchResNet34 = "resnet34_v2";

    public static string ResolveArch(string? modelType)
    {
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return ArchResNet18;
        }

        var normalized = modelType.Trim().ToLowerInvariant();
        if (normalized is ArchResNetTiny or ArchResNet18 or ArchResNet34)
        {
            return normalized;
        }

        if (normalized is "resnet-tiny" or "resnettiny")
        {
            return ArchResNetTiny;
        }

        if (normalized is "resnet34" or "resnet-34")
        {
            return ArchResNet34;
        }

        return ArchResNet18;
    }

    public static torch.nn.Module<torch.Tensor, torch.Tensor> Create(
        string? archName,
        int inputChannels,
        int numClasses,
        int imageSize = 224)
    {
        var resolvedArch = ResolveArch(archName);
        return resolvedArch switch
        {
            ArchResNetTiny => TorchVisionResNetClassifier.CreateTiny(inputChannels, numClasses),
            ArchResNet34 => TorchVisionResNetClassifier.CreateResNet34(inputChannels, numClasses),
            _ => TorchVisionResNetClassifier.CreateResNet18(inputChannels, numClasses)
        };
    }

    /// <summary>Stage widths (stem, layer1..4) for the pretrained slicer; null for non-v2 arches.</summary>
    public static IReadOnlyList<int>? ResolveStageWidths(string? archName)
    {
        return archName?.Trim().ToLowerInvariant() switch
        {
            ArchResNetTiny => [16, 16, 32, 64, 128],
            ArchResNet18 => [64, 64, 128, 256, 512],
            ArchResNet34 => [64, 64, 128, 256, 512],
            _ => null
        };
    }
}
