namespace DeepVisionSharp.Application.Models;

/// <summary>
/// Product model-type keys. 1.0 exposes three ResNet classification sizes.
/// </summary>
public static class ModelTypeNames
{
    public const string ResNetTiny = "resnet-tiny";
    public const string ResNet18 = "resnet18";
    public const string ResNet34 = "resnet34";
    public const string Default = ResNet18;

    private static readonly string[] ClassificationModelTypes =
    [
        ResNetTiny,
        ResNet18,
        ResNet34
    ];

    /// <summary>All creatable product keys.</summary>
    public static IReadOnlyList<string> AllProductKeys { get; } = ClassificationModelTypes;

    /// <summary>Canonicalizes a product model type. Empty input fails.</summary>
    public static bool TryNormalize(string? modelType, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return false;
        }

        var lower = modelType.Trim().ToLowerInvariant();
        if (ClassificationModelTypes.Contains(lower, StringComparer.Ordinal))
        {
            normalized = lower;
            return true;
        }

        return false;
    }

    public static bool IsClassificationModelType(string? modelType) =>
        TryNormalize(modelType, out _);

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
            _ => normalized
        };
    }
}
