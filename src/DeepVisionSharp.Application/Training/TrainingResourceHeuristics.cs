using DeepVisionSharp.Application.Models;

namespace DeepVisionSharp.Application.Training;

/// <summary>
/// Rule-of-thumb guard for training memory.
/// </summary>
/// <remarks>
/// The ResNet v2 stem keeps the first stage at full input resolution, so activation cost grows with
/// <c>batch x width x height</c> and with the channel width of the backbone. This is a warning, not a
/// limit: the operator may know the machine better than the heuristic does. The reference point is
/// a mid-range 8 GB card running ResNet18 at 256x256 with batch 8.
/// </remarks>
public static class TrainingResourceHeuristics
{
    /// <summary>
    /// Units above which the configuration is flagged. Calibrated against the stated reference point
    /// (ResNet18 at 256x256 with batch 8 = 33.5M units, comfortable on 8 GB) so that roughly triple
    /// that — batch 32, or a larger input at a moderate batch — is the point where it warns.
    /// </summary>
    private const long WarnThresholdUnits = 100_000_000;

    /// <summary>Returns a warning message when the configuration looks too large, otherwise null.</summary>
    public static string? DescribeMemoryRisk(string? modelType, int width, int height, int batchSize)
    {
        if (width <= 0 || height <= 0 || batchSize <= 0)
        {
            return null;
        }

        var channelWeight = ChannelWeight(modelType);
        if (channelWeight == 0)
        {
            return null;
        }

        var units = (long)batchSize * width * height * channelWeight;
        if (units <= WarnThresholdUnits)
        {
            return null;
        }

        return $"当前配置（{width}×{height}、batch {batchSize}、{ModelTypeNames.GetDisplayLabel(modelType)}）"
            + "的显存占用可能超出显卡容量，建议降低批量或输入尺寸。";
    }

    /// <summary>
    /// Relative activation width per product model tier. ResNet activations grow with depth, so the
    /// weight tracks the stage widths the classifier factory builds (16 / 64 / 128 channels).
    /// </summary>
    private static int ChannelWeight(string? modelType)
    {
        if (!ModelTypeNames.TryNormalize(modelType, out var normalized))
        {
            return 0;
        }

        return normalized switch
        {
            ModelTypeNames.ResNetTiny => 16,
            ModelTypeNames.ResNet18 => 64,
            ModelTypeNames.ResNet34 => 128,
            _ => 0
        };
    }
}
