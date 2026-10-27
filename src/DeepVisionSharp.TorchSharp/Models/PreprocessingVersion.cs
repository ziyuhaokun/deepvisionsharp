namespace DeepVisionSharp.TorchSharp.Models;

/// <summary>
/// How pixel values are normalised before the network sees them. Recorded in the model sidecar so
/// inference always matches training; models written before this field existed are unit-scale.
/// </summary>
public enum PreprocessingVersion
{
    /// <summary>Divide by 255 only. Every model trained before the v2 pipeline used this.</summary>
    UnitScaleV1 = 0,

    /// <summary>ImageNet mean/std after the /255 step; required by torchvision pretrained weights.</summary>
    ImageNetV2 = 1
}

public static class PreprocessingVersions
{
    public const string UnitScaleWire = "unit_scale_v1";

    public const string ImageNetWire = "imagenet_v2";

    public static string ToWireValue(this PreprocessingVersion version) =>
        version == PreprocessingVersion.ImageNetV2 ? ImageNetWire : UnitScaleWire;

    /// <summary>Unknown or missing values fall back to the legacy pipeline, never to ImageNet.</summary>
    public static PreprocessingVersion Parse(string? value) =>
        string.Equals(value?.Trim(), ImageNetWire, StringComparison.OrdinalIgnoreCase)
            ? PreprocessingVersion.ImageNetV2
            : PreprocessingVersion.UnitScaleV1;
}
