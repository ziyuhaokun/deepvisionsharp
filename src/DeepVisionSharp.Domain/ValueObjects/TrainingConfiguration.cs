namespace DeepVisionSharp.Domain.ValueObjects;

public sealed class TrainingConfiguration
{
    public TrainingConfiguration(
        int epochs,
        int batchSize,
        double learningRate,
        string optimizer,
        double validationRatio = 0.1,
        Guid? galleryId = null,
        IReadOnlyList<Guid>? galleryIds = null,
        TrainingDatasetSplitMode datasetSplitMode = TrainingDatasetSplitMode.AutoSplit,
        IReadOnlyDictionary<Guid, GalleryDatasetRole>? galleryRoles = null,
        ClassificationAugmentation? classificationAugmentation = null,
        int? networkInputSize = null,
        int? networkInputWidth = null,
        int? networkInputHeight = null,
        int earlyStoppingPatience = 0)
    {
        Epochs = epochs;
        EarlyStoppingPatience = Math.Max(0, earlyStoppingPatience);
        BatchSize = batchSize;
        LearningRate = learningRate;
        Optimizer = optimizer;
        ValidationRatio = validationRatio;
        GalleryId = galleryId;
        GalleryIds = galleryIds is { Count: > 0 }
            ? galleryIds.Distinct().ToArray()
            : galleryId is Guid id
                ? [id]
                : Array.Empty<Guid>();
        DatasetSplitMode = datasetSplitMode;
        GalleryRoles = galleryRoles is { Count: > 0 }
            ? new Dictionary<Guid, GalleryDatasetRole>(galleryRoles)
            : new Dictionary<Guid, GalleryDatasetRole>();
        ClassificationAugmentation = classificationAugmentation is null
            ? null
            : ClassificationAugmentation.Normalize(classificationAugmentation);
        var width = networkInputWidth is > 0
            ? networkInputWidth
            : networkInputSize is > 0 ? networkInputSize : null;
        var height = networkInputHeight is > 0
            ? networkInputHeight
            : networkInputSize is > 0 ? networkInputSize : null;
        NetworkInputWidth = width;
        NetworkInputHeight = height;
        NetworkInputSize = width is int w && height is int h && w == h
            ? w
            : width is int onlyW && height is null
                ? onlyW
                : height is int onlyH && width is null
                    ? onlyH
                    : networkInputSize is > 0 ? networkInputSize : null;
    }

    public int Epochs { get; }

    public int BatchSize { get; }

    /// <summary>Stop after this many epochs without validation improvement; 0 keeps fixed epochs.</summary>
    public int EarlyStoppingPatience { get; }

    public double LearningRate { get; }

    public string Optimizer { get; }

    public double ValidationRatio { get; }

    public Guid? GalleryId { get; }

    public IReadOnlyList<Guid> GalleryIds { get; }

    public TrainingDatasetSplitMode DatasetSplitMode { get; }

    public IReadOnlyDictionary<Guid, GalleryDatasetRole> GalleryRoles { get; }

    /// <summary>
    /// Network input edge length when square.
    /// Prefer <see cref="NetworkInputWidth"/> × <see cref="NetworkInputHeight"/>.
    /// </summary>
    public int? NetworkInputSize { get; }

    public int? NetworkInputWidth { get; }

    public int? NetworkInputHeight { get; }

    /// <summary>Classification flip augmentation. Null means the caller did not set one.</summary>
    public ClassificationAugmentation? ClassificationAugmentation { get; }
}
