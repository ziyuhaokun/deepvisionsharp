namespace DeepVisionSharp.Application.Models;

/// <summary>
/// Product model-type keys. Exposes ResNet and YOLO26 classification sizes.
/// </summary>
public static class ModelTypeNames
{
    public const string ResNetTiny = "resnet-tiny";
    public const string ResNet18 = "resnet18";
    public const string ResNet34 = "resnet34";
    public const string Yolo26NCls = "yolo26n-cls";
    public const string Yolo26SCls = "yolo26s-cls";
    public const string Yolo26MCls = "yolo26m-cls";
    public const string Yolo26LCls = "yolo26l-cls";
    public const string Yolo26XCls = "yolo26x-cls";

    public const string Yolo26NDet = "yolo26n-det";
    public const string Yolo26SDet = "yolo26s-det";
    public const string Yolo26MDet = "yolo26m-det";
    public const string Yolo26LDet = "yolo26l-det";
    public const string Yolo26XDet = "yolo26x-det";

    public const string Default = ResNet18;

    public const string DefaultDetection = Yolo26NDet;

    private static readonly string[] ClassificationModelTypes =
    [
        ResNetTiny,
        ResNet18,
        ResNet34,
        Yolo26NCls,
        Yolo26SCls,
        Yolo26MCls,
        Yolo26LCls,
        Yolo26XCls
    ];

    private static readonly string[] DetectionModelTypes =
    [
        Yolo26NDet,
        Yolo26SDet,
        Yolo26MDet,
        Yolo26LDet,
        Yolo26XDet
    ];

    private static readonly string[] KnownModelTypes =
    [
        .. ClassificationModelTypes,
        .. DetectionModelTypes
    ];

    /// <summary>All creatable product keys.</summary>
    public static IReadOnlyList<string> AllProductKeys => KnownModelTypes;

    /// <summary>Classification model keys, in display order.</summary>
    public static IReadOnlyList<string> ClassificationProductKeys => ClassificationModelTypes;

    /// <summary>Detection model keys, in display order.</summary>
    public static IReadOnlyList<string> DetectionProductKeys => DetectionModelTypes;

    /// <summary>Canonicalizes a product model type. Empty input fails.</summary>
    public static bool TryNormalize(string? modelType, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return false;
        }

        var lower = modelType.Trim().ToLowerInvariant();
        if (KnownModelTypes.Contains(lower, StringComparer.Ordinal))
        {
            normalized = lower;
            return true;
        }

        var mapped = lower switch
        {
            "resnet-18" or "resnet_18" or "resnet18_v2" => ResNet18,
            "resnettiny" or "resnet_tiny" or "resnet_tiny_v2" => ResNetTiny,
            "resnet-34" or "resnet_34" or "resnet34_v2" => ResNet34,
            "yolo26n" or "yolo26-n" or "yolo26_n" or "yolo26n_cls" => Yolo26NCls,
            "yolo26s" or "yolo26-s" or "yolo26_s" or "yolo26s_cls" => Yolo26SCls,
            "yolo26m" or "yolo26-m" or "yolo26_m" or "yolo26m_cls" => Yolo26MCls,
            "yolo26l" or "yolo26-l" or "yolo26_l" or "yolo26l_cls" => Yolo26LCls,
            "yolo26x" or "yolo26-x" or "yolo26_x" or "yolo26x_cls" => Yolo26XCls,
            "yolo26n-det" or "yolo26-n-det" or "yolo26n_det" => Yolo26NDet,
            "yolo26s-det" or "yolo26-s-det" or "yolo26s_det" => Yolo26SDet,
            "yolo26m-det" or "yolo26-m-det" or "yolo26m_det" => Yolo26MDet,
            "yolo26l-det" or "yolo26-l-det" or "yolo26l_det" => Yolo26LDet,
            "yolo26x-det" or "yolo26-x-det" or "yolo26x_det" => Yolo26XDet,
            _ => null
        };

        if (mapped is not null)
        {
            normalized = mapped;
            return true;
        }

        return false;
    }

    /// <summary>True for any product model key (classification or detection).</summary>
    public static bool IsKnownModelType(string? modelType) =>
        TryNormalize(modelType, out _);

    public static bool IsClassificationModelType(string? modelType) =>
        TryNormalize(modelType, out var normalized)
        && ClassificationModelTypes.Contains(normalized, StringComparer.Ordinal);

    public static bool IsDetectionModelType(string? modelType) =>
        TryNormalize(modelType, out var normalized)
        && DetectionModelTypes.Contains(normalized, StringComparer.Ordinal);

    /// <summary>Short UI label for a model type key.</summary>
    public static string GetDisplayLabel(string? modelType)
    {
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return "未知";
        }

        if (!TryNormalize(modelType, out var normalized))
        {
            return modelType.Trim();
        }

        return normalized switch
        {
            ResNetTiny => "ResNet-Tiny",
            ResNet18 => "ResNet-18",
            ResNet34 => "ResNet-34",
            Yolo26NCls => "YOLO26-N",
            Yolo26SCls => "YOLO26-S",
            Yolo26MCls => "YOLO26-M",
            Yolo26LCls => "YOLO26-L",
            Yolo26NDet => "YOLO26-N 检测",
            Yolo26SDet => "YOLO26-S 检测",
            Yolo26MDet => "YOLO26-M 检测",
            Yolo26LDet => "YOLO26-L 检测",
            Yolo26XDet => "YOLO26-X 检测",
            Yolo26XCls => "YOLO26-X",
            _ => normalized
        };
    }
}

public sealed record ModelTypeOption(string Value, string Label);
