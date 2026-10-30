namespace DeepVisionSharp.Application.Training;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;

public sealed class TrainingJobForm
{
    public string ModelName { get; set; } = string.Empty;

    public string ModelType { get; set; } = ModelTypeNames.Default;

    public List<Guid> SelectedGalleryIds { get; set; } = new();

    public Guid? SelectedGalleryId { get; set; }

    public TrainingDatasetSplitMode DatasetSplitMode { get; set; } = TrainingDatasetSplitMode.AutoSplit;

    public Dictionary<Guid, GalleryDatasetRole> GalleryRoles { get; set; } = new();

    public int Epochs { get; set; } = 50;

    // Defaults are deliberately conservative: the ResNet v2 stem keeps the first stage at full
    // input resolution, so 512x512 x batch 16 is an order of magnitude heavier than 256x256 x 8.
    public int BatchSize { get; set; } = 8;

    /// <summary>Stop after this many epochs without validation improvement; 0 disables early stopping.</summary>
    public int EarlyStoppingPatience { get; set; }

    public double Lr { get; set; } = 0.005;

    public string Optimizer { get; set; } = "sgd";

    /// <summary>Network input width. Prefer over square <see cref="NetworkInputSize"/>.</summary>
    public int NetworkInputWidth { get; set; } = 256;

    /// <summary>Network input height. Prefer over square <see cref="NetworkInputSize"/>.</summary>
    public int NetworkInputHeight { get; set; } = 256;

    /// <summary>Legacy square edge. Reading returns max(W,H); writing sets both W and H.</summary>
    public int NetworkInputSize
    {
        get => Math.Max(NetworkInputWidth, NetworkInputHeight);
        set
        {
            NetworkInputWidth = value;
            NetworkInputHeight = value;
        }
    }

    public double ValidationRatio { get; set; } = 0.1;

    /// <summary>Classification flip augmentation.</summary>
    public ClassificationAugmentation? ClassificationAugmentation { get; set; }

    // --- Detection-only fields (ignored for classification jobs) ---

    public double DetectionNmsIou { get; set; } = 0.45;

    public double DetectionConfidence { get; set; } = 0.25;

    public int DetectionMaxDetections { get; set; } = 100;

    public bool DetectionHorizontalFlip { get; set; } = true;

    public DetectionTrainingOptions? ToDetectionOptions() =>
        ModelTypeNames.IsDetectionModelType(ModelType)
            ? DetectionTrainingOptions.Normalize(new DetectionTrainingOptions(
                DetectionNmsIou,
                DetectionConfidence,
                DetectionMaxDetections,
                DetectionHorizontalFlip))
            : null;
}

public static class TrainingJobFormMapper
{
    public static TrainingConfiguration ToConfiguration(TrainingJobForm form)
    {
        var galleryIds = ResolveParticipatingGalleryIds(form);
        var roles = NormalizeGalleryRoles(form, galleryIds);
        var classificationAugmentation = ModelTypeNames.IsClassificationModelType(form.ModelType)
            ? ClassificationAugmentation.Normalize(
                form.ClassificationAugmentation ?? ClassificationAugmentation.Disabled)
            : null;

        int? networkInputSize = null;
        int? networkInputWidth = null;
        int? networkInputHeight = null;
        if (NetworkInputSizePolicy.ForModelType(form.ModelType) is not null
            && form.NetworkInputWidth > 0
            && form.NetworkInputHeight > 0)
        {
            networkInputWidth = form.NetworkInputWidth;
            networkInputHeight = form.NetworkInputHeight;
            if (form.NetworkInputWidth == form.NetworkInputHeight)
            {
                networkInputSize = form.NetworkInputWidth;
            }
        }

        return new(
            epochs: form.Epochs,
            batchSize: form.BatchSize,
            earlyStoppingPatience: form.EarlyStoppingPatience,
            learningRate: form.Lr,
            optimizer: form.Optimizer,
            validationRatio: form.ValidationRatio,
            galleryId: galleryIds.Count > 0 ? null : form.SelectedGalleryId,
            galleryIds: galleryIds,
            datasetSplitMode: form.DatasetSplitMode,
            galleryRoles: roles,
            classificationAugmentation: classificationAugmentation,
            networkInputSize: networkInputSize,
            networkInputWidth: networkInputWidth,
            networkInputHeight: networkInputHeight,
            detectionOptions: form.ToDetectionOptions());
    }

    public static TrainingJobForm FromConfiguration(
        TrainingConfiguration config,
        string modelType,
        string modelName)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!ModelTypeNames.TryNormalize(modelType, out var normalized)
            || !ModelCapabilityCatalog.TryResolve(normalized, out _))
        {
            throw new InvalidOperationException($"不支持的训练模型类型：{modelType}");
        }

        var galleryIds = config.GalleryIds?.ToList() ?? new List<Guid>();
        var roles = config.GalleryRoles?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<Guid, GalleryDatasetRole>();
        var defaultInput = NetworkInputSizePolicy.ForModelType(normalized)?.Default ?? 256;

        return new TrainingJobForm
        {
            ModelName = modelName,
            ModelType = normalized,
            DatasetSplitMode = config.DatasetSplitMode,
            SelectedGalleryIds = galleryIds,
            SelectedGalleryId = config.GalleryId,
            GalleryRoles = roles,
            Epochs = config.Epochs,
            BatchSize = config.BatchSize,
            EarlyStoppingPatience = config.EarlyStoppingPatience,
            Lr = config.LearningRate,
            Optimizer = config.Optimizer,
            ValidationRatio = config.ValidationRatio,
            NetworkInputWidth = config.NetworkInputWidth ?? config.NetworkInputSize ?? defaultInput,
            NetworkInputHeight = config.NetworkInputHeight ?? config.NetworkInputSize ?? defaultInput,
            ClassificationAugmentation = config.ClassificationAugmentation,
            DetectionNmsIou = config.DetectionOptions?.NmsIouThreshold ?? 0.45,
            DetectionConfidence = config.DetectionOptions?.EvaluationConfidence ?? 0.25,
            DetectionMaxDetections = config.DetectionOptions?.MaxDetections ?? 100,
            DetectionHorizontalFlip = config.DetectionOptions?.HorizontalFlip ?? true
        };
    }

    public static IReadOnlyList<Guid> ResolveParticipatingGalleryIds(TrainingJobForm form)
    {
        if (form.DatasetSplitMode == TrainingDatasetSplitMode.PurposeAssignment)
        {
            return form.GalleryRoles
                .Where(pair => pair.Value != GalleryDatasetRole.None)
                .Select(pair => pair.Key)
                .Distinct()
                .ToArray();
        }

        var selected = form.SelectedGalleryIds?
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToArray()
            ?? Array.Empty<Guid>();

        if (selected.Length > 0)
        {
            return selected;
        }

        return form.SelectedGalleryId is Guid legacyId && legacyId != Guid.Empty
            ? [legacyId]
            : Array.Empty<Guid>();
    }

    private static Dictionary<Guid, GalleryDatasetRole> NormalizeGalleryRoles(
        TrainingJobForm form,
        IReadOnlyList<Guid> participatingIds)
    {
        if (form.DatasetSplitMode != TrainingDatasetSplitMode.PurposeAssignment)
        {
            return new Dictionary<Guid, GalleryDatasetRole>();
        }

        var roles = new Dictionary<Guid, GalleryDatasetRole>();
        foreach (var galleryId in participatingIds)
        {
            if (form.GalleryRoles.TryGetValue(galleryId, out var role)
                && role != GalleryDatasetRole.None)
            {
                roles[galleryId] = role;
            }
        }

        return roles;
    }
}

public sealed class TrainingJobRow
{
    public Guid Id { get; init; }

    public string Name { get; set; } = string.Empty;

    public string ModelType { get; init; } = ModelTypeNames.Default;

    public TrainingJobStatus Status { get; init; } = TrainingJobStatus.Draft;

    public DateTime? StartedAt { get; init; }

    public DateTime? FinishedAt { get; init; }

    public string? LogFile { get; init; }

    public string? BestModelPath { get; init; }

    public string? LastModelPath { get; init; }

    public string? OutputRoot { get; init; }

    public string? FailureMessage { get; init; }

    public TrainingDatasetSplitMode? ConfigDatasetSplitMode { get; init; }

    public IReadOnlyDictionary<Guid, GalleryDatasetRole>? ConfigGalleryRoles { get; init; }

    public int? ConfigEpochs { get; init; }

    public int? ConfigEarlyStoppingPatience { get; init; }

    public int? ConfigBatchSize { get; init; }

    public double? ConfigLearningRate { get; init; }

    public string? ConfigOptimizer { get; init; }

    public Guid? ConfigGalleryId { get; init; }

    public IReadOnlyList<Guid>? ConfigGalleryIds { get; init; }

    public double? ConfigValidationRatio { get; init; }

    public ClassificationAugmentation? ConfigClassificationAugmentation { get; init; }

    public DetectionTrainingOptions? ConfigDetectionOptions { get; init; }

    public int? ConfigNetworkInputSize { get; init; }

    public int? ConfigNetworkInputWidth { get; init; }

    public int? ConfigNetworkInputHeight { get; init; }
}
