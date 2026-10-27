using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Training;

public static class TrainingDatasetConfigRules
{
    public sealed record DatasetEstimate(int TrainSamples, int ValSamples, string? ErrorMessage)
    {
        public bool IsValid => TrainSamples > 0 && ValSamples > 0 && ErrorMessage is null;
    }

    public static DatasetEstimate Estimate(
        TrainingDatasetSplitMode splitMode,
        IReadOnlyDictionary<Guid, GalleryDatasetRole> galleryRoles,
        IReadOnlyList<Guid> selectedGalleryIds,
        IReadOnlyDictionary<Guid, int> annotatedCountsByGalleryId,
        double validationRatio)
    {
        if (splitMode == TrainingDatasetSplitMode.PurposeAssignment)
        {
            return EstimatePurpose(galleryRoles, annotatedCountsByGalleryId);
        }

        return EstimateAutoSplit(selectedGalleryIds, annotatedCountsByGalleryId, validationRatio);
    }

    /// <summary>
    /// Validates the dataset selection. <paramref name="perClassCounts"/> is optional: when supplied,
    /// every class is also required to have at least two samples, which is what the exporter
    /// enforces (<see cref="DeepVisionSharp.Infrastructure.Services.ClassificationSplitPlanner"/>).
    /// Without it a run whose rarest class has one sample passes validation and only fails after the
    /// job has already entered Preparing.
    /// </summary>
    public static string? ValidateForm(
        TrainingJobForm form,
        IReadOnlyDictionary<Guid, int> annotatedCountsByGalleryId,
        IReadOnlyList<ProjectLabelCountRow>? perClassCounts = null)
    {
        var participating = TrainingJobFormMapper.ResolveParticipatingGalleryIds(form);
        if (participating.Count == 0)
        {
            return TrainingValidationMessages.SelectDataset;
        }

        foreach (var galleryId in participating)
        {
            if (!annotatedCountsByGalleryId.TryGetValue(galleryId, out var annotated) || annotated <= 0)
            {
                return "所选数据集必须包含已标注样本";
            }
        }

        if (form.DatasetSplitMode == TrainingDatasetSplitMode.AutoSplit
            && (form.ValidationRatio < 0 || form.ValidationRatio > 0.5))
        {
            return TrainingValidationMessages.ValidationRatioRange;
        }

        var estimate = Estimate(
            form.DatasetSplitMode,
            form.GalleryRoles,
            participating,
            annotatedCountsByGalleryId,
            form.ValidationRatio);
        if (estimate.ErrorMessage is not null)
        {
            return estimate.ErrorMessage;
        }

        if (perClassCounts is { Count: > 0 })
        {
            if (perClassCounts.Count < 2)
            {
                return "分类训练至少需要 2 个类别";
            }

            var tooSmall = perClassCounts.FirstOrDefault(row => row.SampleCount < 2);
            if (tooSmall is not null)
            {
                return $"类别「{tooSmall.Label}」只有 {tooSmall.SampleCount} 个样本，"
                    + "无法同时建立独立的训练集与验证集；请至少补充到 2 个。";
            }
        }

        return null;
    }

    private static DatasetEstimate EstimatePurpose(
        IReadOnlyDictionary<Guid, GalleryDatasetRole> galleryRoles,
        IReadOnlyDictionary<Guid, int> annotatedCountsByGalleryId)
    {
        var train = 0;
        var val = 0;

        foreach (var (galleryId, role) in galleryRoles)
        {
            if (!annotatedCountsByGalleryId.TryGetValue(galleryId, out var annotated) || annotated <= 0)
            {
                continue;
            }

            switch (role)
            {
                case GalleryDatasetRole.Training:
                    train += annotated;
                    break;
                case GalleryDatasetRole.Validation:
                    val += annotated;
                    break;
                case GalleryDatasetRole.TrainingAndValidation:
                    train += annotated;
                    val += annotated;
                    break;
            }
        }

        if (train <= 0 || val <= 0)
        {
            return new DatasetEstimate(train, val, "训练与验证样本均需大于 0");
        }

        return new DatasetEstimate(train, val, null);
    }

    private static DatasetEstimate EstimateAutoSplit(
        IReadOnlyList<Guid> selectedGalleryIds,
        IReadOnlyDictionary<Guid, int> annotatedCountsByGalleryId,
        double validationRatio)
    {
        var total = selectedGalleryIds
            .Distinct()
            .Sum(id => annotatedCountsByGalleryId.TryGetValue(id, out var count) ? count : 0);

        if (total <= 0)
        {
            return new DatasetEstimate(0, 0, "所选数据集必须包含已标注样本");
        }

        var ratio = Math.Clamp(validationRatio, 0, 0.5);
        var val = (int)Math.Round(total * ratio);
        var train = total - val;

        if (train <= 0 || val <= 0)
        {
            return new DatasetEstimate(train, val, "请调整验证集比例，确保训练与验证样本均大于 0");
        }

        return new DatasetEstimate(train, val, null);
    }
}
