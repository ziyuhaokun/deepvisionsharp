using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>
/// Automatic train/val split for detection. Unlike classification it does not group by class:
/// a sample can carry several classes or none at all. Ordering reuses the classification rank key,
/// so the same corpus splits the same way regardless of enumeration order.
/// </summary>
internal static class DetectionSplitPlanner
{
    public static Result<bool[]> AssignValidationByRank(
        IReadOnlyList<(Guid GalleryId, string FileName)> samples,
        double validationRatio)
    {
        if (samples.Count < 2)
        {
            return Result<bool[]>.Failure("目标检测训练至少需要 2 张已标注图像（一张用于验证）。");
        }

        var ratio = Math.Clamp(validationRatio, 0.05, 0.95);
        var validationCount = Math.Clamp((int)Math.Round(samples.Count * ratio), 1, samples.Count - 1);
        var validation = new bool[samples.Count];
        var ranked = Enumerable.Range(0, samples.Count)
            .Select(index => (
                Index: index,
                Rank: ClassificationSplitPlanner.RankKey(samples[index].GalleryId, samples[index].FileName),
                samples[index].FileName))
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.FileName, StringComparer.Ordinal)
            .ToArray();

        for (var position = 0; position < validationCount; position++)
        {
            validation[ranked[position].Index] = true;
        }

        return Result<bool[]>.Success(validation);
    }
}
