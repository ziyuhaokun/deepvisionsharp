using TorchSharp;
using DeepVisionSharp.TorchSharp.Classification.Yolo26;
using DeepVisionSharp.TorchSharp.Classification.ResNet;

namespace DeepVisionSharp.TorchSharp.Classification;

public static class TorchSharpClassifierFactory
{
    // torchvision-compatible: stride-2 stem + maxpool and torchvision module names, so the
    // ImageNet state dict applies without key mapping.
    public const string ArchResNetTiny = "resnet_tiny_v2";
    public const string ArchResNet18 = "resnet18_v2";
    public const string ArchResNet34 = "resnet34_v2";
    public const string ArchYolo26NCls = "yolo26n-cls";
    public const string ArchYolo26SCls = "yolo26s-cls";
    public const string ArchYolo26MCls = "yolo26m-cls";
    public const string ArchYolo26LCls = "yolo26l-cls";
    public const string ArchYolo26XCls = "yolo26x-cls";

    public static string ResolveArch(string? modelType)
    {
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return ArchResNet18;
        }

        var normalized = modelType.Trim().ToLowerInvariant();
        if (normalized is ArchResNetTiny or ArchResNet18 or ArchResNet34
            or ArchYolo26NCls or ArchYolo26SCls or ArchYolo26MCls or ArchYolo26LCls or ArchYolo26XCls)
        {
            return normalized;
        }

        var canonical = normalized.Replace('_', '-');
        if (canonical.EndsWith("-v2", StringComparison.Ordinal))
        {
            canonical = canonical[..^"-v2".Length];
        }

        return canonical switch
        {
            "resnet-tiny" or "resnettiny" => ArchResNetTiny,
            "resnet18" or "resnet-18" => ArchResNet18,
            "resnet34" or "resnet-34" => ArchResNet34,
            "yolo26n" or "yolo26-n" or "yolo26n-cls" or "yolo26-n-cls" => ArchYolo26NCls,
            "yolo26s" or "yolo26-s" or "yolo26s-cls" or "yolo26-s-cls" => ArchYolo26SCls,
            "yolo26m" or "yolo26-m" or "yolo26m-cls" or "yolo26-m-cls" => ArchYolo26MCls,
            "yolo26l" or "yolo26-l" or "yolo26l-cls" or "yolo26-l-cls" => ArchYolo26LCls,
            "yolo26x" or "yolo26-x" or "yolo26x-cls" or "yolo26-x-cls" => ArchYolo26XCls,
            _ => ArchResNet18
        };
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
            ArchYolo26NCls or ArchYolo26SCls or ArchYolo26MCls or ArchYolo26LCls or ArchYolo26XCls =>
                Yolo26ClsClassifier.Create(resolvedArch, inputChannels, numClasses),
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
