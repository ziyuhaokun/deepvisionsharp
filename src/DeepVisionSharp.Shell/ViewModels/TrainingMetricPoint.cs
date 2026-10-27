namespace DeepVisionSharp.Shell.ViewModels;

public sealed class TrainingMetricPoint
{
    public int Epoch { get; init; }

    public double? TrainLoss { get; set; }

    public double? TrainBoxLoss { get; set; }

    public double? TrainClsLoss { get; set; }

    public double? TrainDflLoss { get; set; }

    public double? ValLoss { get; set; }

    public double? ValBoxLoss { get; set; }

    public double? ValClsLoss { get; set; }

    public double? ValDflLoss { get; set; }

    public double? ValMap { get; set; }

    public double? ValMap50 { get; set; }

    /// <summary>Legacy instance-segmentation mask mAP50-95 (0–1).</summary>
    public double? MaskMap { get; set; }

    /// <summary>Legacy instance-segmentation mask mAP50 (0–1).</summary>
    public double? MaskMap50 { get; set; }

    /// <summary>Legacy instance-segmentation box mAP50-95 (0–1).</summary>
    public double? BoxMap { get; set; }

    /// <summary>Legacy instance-segmentation box mAP50 (0–1).</summary>
    public double? BoxMap50 { get; set; }

    /// <summary>UNet semantic mIoU (0–1).</summary>
    public double? Miou { get; set; }

    /// <summary>UNet semantic mean Dice (0–1).</summary>
    public double? Dice { get; set; }

    /// <summary>UNet semantic pixel accuracy (0–1).</summary>
    public double? PixelAccuracy { get; set; }

    /// <summary>Classification Top-1 accuracy (0–1). Prefer over ValMap50 for CLS UI.</summary>
    public double? AccuracyTop1 { get; set; }

    /// <summary>Classification Top-5 accuracy (0–1). Less meaningful for 2-class OK/NG.</summary>
    public double? AccuracyTop5 { get; set; }

    public double? Precision { get; set; }

    public double? Recall { get; set; }

    public double? Lr { get; set; }

    /// <summary>Per-class validation metrics for this epoch (classification only).</summary>
    public IReadOnlyList<TrainingPerClassPoint>? PerClass { get; set; }

    /// <summary>Effective primary quality metric for ranking (Top-1, Mask mAP50, mIoU, or mAP50).</summary>
    public double? PrimaryQuality => AccuracyTop1 ?? MaskMap50 ?? Miou ?? ValMap50;
}

/// <summary>One class' precision/recall/F1 in a validation pass.</summary>
public sealed record TrainingPerClassPoint(string ClassName, double Precision, double Recall, double F1, int Support);

public sealed record TrainingChartPoint(int Epoch, double Value);

public sealed record TrainingChartSeries(string Name, IReadOnlyList<TrainingChartPoint> Points);

/// <summary>Row of the per-class panel; the latest epoch that reported metrics wins.</summary>
public sealed class TrainingPerClassRow
{
    public string ClassName { get; init; } = string.Empty;

    public string PrecisionText { get; init; } = "—";

    public string RecallText { get; init; } = "—";

    public string F1Text { get; init; } = "—";

    public string SupportText { get; init; } = "—";
}

public sealed class TrainingMetricHistoryRow
{
    public int Epoch { get; init; }

    public string TrainLossText { get; init; } = "—";

    public string ValLossText { get; init; } = "—";

    public string ValMapText { get; init; } = "—";

    public string ValMap50Text { get; init; } = "—";

    public string MaskMapText { get; init; } = "—";

    public string MaskMap50Text { get; init; } = "—";

    public string BoxMapText { get; init; } = "—";

    public string BoxMap50Text { get; init; } = "—";

    public string AccuracyTop1Text { get; init; } = "—";

    public string AccuracyTop5Text { get; init; } = "—";

    public string PrecisionText { get; init; } = "—";

    public string RecallText { get; init; } = "—";

    public string LrText { get; init; } = "—";

    public bool IsBestMap50 { get; init; }
}
