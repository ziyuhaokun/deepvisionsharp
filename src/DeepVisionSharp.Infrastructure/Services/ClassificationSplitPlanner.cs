using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

/// <summary>A sample as seen by the automatic train/val split.</summary>
internal sealed record ClassificationSplitCandidate(Guid GalleryId, string FileName, string ImageClass);

/// <summary>
/// Automatic train/val split that depends only on each sample's own identity.
/// </summary>
/// <remarks>
/// The previous split shuffled every class with <c>seed + classIndex</c>: the class index followed the
/// enumeration order of galleries and samples, and adding one image reshuffled the whole class, so
/// two runs on almost the same data validated on almost disjoint sets. Here every sample gets a rank
/// key hashed from (gallery, file name); each class sends its lowest-ranked samples to validation.
/// The result ignores input order, and adding or removing one sample moves at most one other sample
/// between train and val.
/// </remarks>
internal static class ClassificationSplitPlanner
{
    /// <summary>Hash salt; recorded in dataset-manifest.json so a split can be reproduced.</summary>
    internal const int Seed = 20261019;

    /// <summary>Recorded in dataset-manifest.json; bump when the assignment rule changes.</summary>
    internal const string Method = "hash-rank-v1";

    /// <summary>
    /// Returns one validation flag per sample, in input order. Every class keeps at least one train
    /// and one validation sample, so a class with a single sample is rejected.
    /// </summary>
    public static Result<bool[]> AssignValidation(
        IReadOnlyList<ClassificationSplitCandidate> samples,
        double validationRatio)
    {
        var ratio = Math.Clamp(validationRatio, 0.05, 0.95);
        var isValidation = new bool[samples.Count];
        var groups = Enumerable.Range(0, samples.Count)
            .GroupBy(index => samples[index].ImageClass, StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var ranked = group
                .Select(index => (Index: index, Rank: RankKey(samples[index].GalleryId, samples[index].FileName)))
                .OrderBy(item => item.Rank)
                // Equal ranks only come from file names that differ by case; keep them ordered.
                .ThenBy(item => samples[item.Index].FileName, StringComparer.Ordinal)
                .ToArray();
            if (ranked.Length == 1)
            {
                return Result<bool[]>.Failure(
                    $"类别「{group.Key}」只有 1 个样本，无法同时建立独立 train/val 集；请至少提供 2 个样本，或使用明确的训练/验证图库。");
            }

            var validationCount = Math.Clamp((int)Math.Round(ranked.Length * ratio), 1, ranked.Length - 1);
            for (var position = 0; position < validationCount; position++)
            {
                isValidation[ranked[position].Index] = true;
            }
        }

        return Result<bool[]>.Success(isValidation);
    }

    /// <summary>
    /// Stable per-sample rank. File names are lower-cased because Windows treats them
    /// case-insensitively; the class is not part of the key, so relabelling keeps the rank.
    /// </summary>
    internal static ulong RankKey(Guid galleryId, string fileName)
    {
        var identity = $"{Seed}|{galleryId:N}|{fileName.ToLowerInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return BinaryPrimitives.ReadUInt64BigEndian(hash);
    }
}
