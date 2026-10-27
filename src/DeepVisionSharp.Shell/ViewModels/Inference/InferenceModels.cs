using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.ViewModels.Inference;

public enum InferenceImageSourceMode
{
    ExternalFiles,
    GallerySamples
}

public sealed record InferencePageModelOption(
    Guid Id,
    string Name,
    string ModelType,
    string ModelPath)
{
    public string DisplayText =>
        $"{Name} ({ModelTypeNames.GetDisplayLabel(ModelType)})";

    public string SpatialSummary => string.Empty;

    public bool ShowsSpatialSummary => false;
}

/// <summary>
/// One image in the inference worklist, with optional OK/NG expected vs predicted marks
/// for session-level miss-rate evaluation (not written back to gallery).
/// </summary>
public sealed partial class InferenceImageItem : ObservableObject
{
    public required string ImagePath { get; init; }

    public required string DisplayName { get; init; }

    public required InferenceImageSourceMode SourceMode { get; init; }

    public Guid? SampleId { get; init; }

    public Guid? GalleryId { get; init; }

    /// <summary>Gallery display name when loaded from a sample library (for multi-gallery lists).</summary>
    public string? GalleryName { get; init; }

    public ImageSource? Preview { get; set; }

    /// <summary>Ground-truth image class: OK / NG / null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpectedLabelText))]
    [NotifyPropertyChangedFor(nameof(HasExpected))]
    [NotifyPropertyChangedFor(nameof(EvaluationTag))]
    [NotifyPropertyChangedFor(nameof(IsMiss))]
    private string? _expectedClass;

    /// <summary>Model-predicted image class: OK / NG / null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PredictedLabelText))]
    [NotifyPropertyChangedFor(nameof(HasPredicted))]
    [NotifyPropertyChangedFor(nameof(EvaluationTag))]
    [NotifyPropertyChangedFor(nameof(IsMiss))]
    private string? _predictedClass;

    [ObservableProperty]
    private double? _predictedConfidence;

    /// <summary>Wall-clock ms of the last infer for this item (batch report).</summary>
    [ObservableProperty]
    private double? _lastInferElapsedMs;

    [ObservableProperty]
    private string? _lastInferError;

    [ObservableProperty]
    private string? _lastPerformanceText;

    /// <summary>
    /// Cached UI summary for this image so switching samples can restore canvas overlays.
    /// </summary>
    public InferenceResultSummary? LastResultSummary { get; set; }

    public string SourceLabel => SourceMode switch
    {
        InferenceImageSourceMode.ExternalFiles => "外部导入",
        InferenceImageSourceMode.GallerySamples =>
            string.IsNullOrWhiteSpace(GalleryName) ? "样本库" : GalleryName,
        _ => SourceMode.ToString()
    };

    public bool HasExpected => !string.IsNullOrWhiteSpace(ExpectedClass);

    public bool HasPredicted => !string.IsNullOrWhiteSpace(PredictedClass);

    public string ExpectedLabelText => string.IsNullOrWhiteSpace(ExpectedClass) ? "预计—" : $"预计{ExpectedClass}";

    public string PredictedLabelText => string.IsNullOrWhiteSpace(PredictedClass) ? "推理—" : $"推理{PredictedClass}";

    /// <summary>
    /// NG is the positive (defect) class:
    /// 漏检 = expected NG, predicted OK; 误报 = expected OK, predicted NG.
    /// </summary>
    public string EvaluationTag
    {
        get
        {
            if (!HasExpected || !HasPredicted)
            {
                return string.Empty;
            }

            var exp = ExpectedClass!;
            var pred = PredictedClass!;
            if (IsNg(exp) && IsNg(pred))
            {
                return "命中";
            }

            if (IsOk(exp) && IsOk(pred))
            {
                return "正确";
            }

            if (IsNg(exp) && IsOk(pred))
            {
                return "漏检";
            }

            if (IsOk(exp) && IsNg(pred))
            {
                return "误报";
            }

            return string.Equals(exp.Trim(), pred.Trim(), StringComparison.OrdinalIgnoreCase) ? "正确" : "分类错误";
        }
    }

    public bool IsMiss =>
        HasExpected
        && HasPredicted
        && IsNg(ExpectedClass)
        && IsOk(PredictedClass);

    public static bool IsOk(string? value) =>
        string.Equals(value, AnnotationClassLabels.Ok, StringComparison.OrdinalIgnoreCase);

    public static bool IsNg(string? value) =>
        string.Equals(value, AnnotationClassLabels.Ng, StringComparison.OrdinalIgnoreCase);
}

public sealed class InferenceGalleryOption
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public int SampleCount { get; init; }

    public string DisplayText => SampleCount > 0 ? $"{Name}（{SampleCount}）" : Name;
}

public sealed class InferenceDetectionRow
{
    public Guid OverlayId { get; init; } = Guid.NewGuid();

    public required string ClassName { get; init; }

    public required double Confidence { get; init; }

    public string ConfidenceText => $"{Confidence:P1}";
}

/// <summary>Session evaluation with NG as positive class (漏检 = false negative).</summary>
/// <summary>One row in the batch analysis report grid.</summary>
public sealed class InferenceBatchReportSampleRow
{
    public required string DisplayName { get; init; }

    public string SourceLabel { get; init; } = string.Empty;

    public string ExpectedText { get; init; } = "—";

    public string PredictedText { get; init; } = "—";

    public string EvaluationTag { get; init; } = string.Empty;

    public string ElapsedText { get; init; } = "—";

    public string StatusText { get; init; } = string.Empty;

    /// <summary>Numeric elapsed ms for charts; null when not run or failed without timing.</summary>
    public double? ElapsedMs { get; init; }

    public bool IsSuccess { get; init; }

    /// <summary>Absolute image path for original-sample gallery / PDF export.</summary>
    public string ImagePath { get; init; } = string.Empty;
}

/// <summary>Original-image sample card in the batch report (OK / NG / 漏检 / 误报).</summary>
public sealed class InferenceReportOriginalSample
{
    /// <summary>ok | ng | miss | false_alarm</summary>
    public required string Role { get; init; }

    public required string Title { get; init; }

    public required string Verdict { get; init; }

    public required string ImagePath { get; init; }

    public ImageSource? Thumbnail { get; init; }

    public string BorderBrushKey => Role switch
    {
        "ok" => "#34C759",
        "ng" => "#FF3B30",
        "miss" => "#FF9F0A",
        "false_alarm" => "#AF52DE",
        _ => "#8E8E93",
    };
}

/// <summary>Session report after「推理全部」.</summary>
public sealed class InferenceBatchReport
{
    public const int MaxOriginalSamplesPerRole = 4;

    public required string Headline { get; init; }

    public required string ModelText { get; init; }

    public required string DeviceText { get; init; }

    public required string GeneratedAtText { get; init; }

    public int TotalCount { get; init; }

    public int SuccessCount { get; init; }

    public int FailedCount { get; init; }

    public double TotalWallMilliseconds { get; init; }

    public double AverageSuccessMilliseconds { get; init; }

    public double MinSuccessMilliseconds { get; init; }

    public double MaxSuccessMilliseconds { get; init; }

    public required InferenceOkNgEvaluation Evaluation { get; init; }

    public IReadOnlyList<InferenceBatchReportSampleRow> Samples { get; init; } =
        Array.Empty<InferenceBatchReportSampleRow>();

    /// <summary>Original image examples for OK / NG / miss / false-alarm review.</summary>
    public IReadOnlyList<InferenceReportOriginalSample> OriginalSamples { get; init; } =
        Array.Empty<InferenceReportOriginalSample>();

    public bool HasOriginalSamples => OriginalSamples.Count > 0;

    public IReadOnlyList<InferenceReportOriginalSample> OkOriginalSamples =>
        OriginalSamples.Where(s => s.Role == "ok").ToArray();

    public IReadOnlyList<InferenceReportOriginalSample> NgOriginalSamples =>
        OriginalSamples.Where(s => s.Role == "ng").ToArray();

    public IReadOnlyList<InferenceReportOriginalSample> MissOriginalSamples =>
        OriginalSamples.Where(s => s.Role == "miss").ToArray();

    public IReadOnlyList<InferenceReportOriginalSample> FalseAlarmOriginalSamples =>
        OriginalSamples.Where(s => s.Role == "false_alarm").ToArray();

    /// <summary>
    /// Prediction anomalies for review: 漏检 (FN) and 误报 (FP).
    /// </summary>
    public IReadOnlyList<InferenceBatchReportSampleRow> AnomalySamples =>
        Samples
            .Where(IsPredictionAnomaly)
            .OrderBy(row => AnomalySortKey(row.EvaluationTag))
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public int AnomalyCount => AnomalySamples.Count;

    public int MissCount =>
        Samples.Count(row => string.Equals(row.EvaluationTag, "漏检", StringComparison.Ordinal));

    public int FalseAlarmCount =>
        Samples.Count(row => string.Equals(row.EvaluationTag, "误报", StringComparison.Ordinal));

    public int ClassificationErrorCount => Samples.Count(row => row.EvaluationTag == "分类错误");

    public string AnomalyTabHeader =>
        AnomalyCount <= 0 ? "预测异常" : $"预测异常 ({AnomalyCount})";

    public string AnomalySummaryText =>
        AnomalyCount <= 0
            ? "无预测异常"
            : $"共 {AnomalyCount} 条 · 分类错误 {ClassificationErrorCount} · 漏检 {MissCount} · 误报 {FalseAlarmCount}";

    public bool HasAnomalySamples => AnomalyCount > 0;

    public static bool IsPredictionAnomaly(InferenceBatchReportSampleRow row) =>
        string.Equals(row.EvaluationTag, "漏检", StringComparison.Ordinal)
        || string.Equals(row.EvaluationTag, "误报", StringComparison.Ordinal)
        || string.Equals(row.EvaluationTag, "分类错误", StringComparison.Ordinal);

    private static int AnomalySortKey(string evaluationTag) =>
        evaluationTag switch
        {
            "漏检" => 0,
            "误报" => 1,
            _ => 9
        };

    public string TotalWallText => $"{TotalWallMilliseconds:F0} ms";

    public string AverageSuccessText =>
        SuccessCount <= 0 ? "—" : $"{AverageSuccessMilliseconds:F1} ms";

    public string MinMaxSuccessText =>
        SuccessCount <= 0
            ? "—"
            : $"{MinSuccessMilliseconds:F1} ~ {MaxSuccessMilliseconds:F1} ms";

    public string SuccessRateText =>
        TotalCount <= 0 ? "—" : $"{(double)SuccessCount / TotalCount:P1}";

    public string MissRateText =>
        Evaluation.PairedCount <= 0
            ? "—（无 OK/NG 成对样本）"
            : $"{Evaluation.MissRate:P1}（FN {Evaluation.FalseNegative} / NG {Evaluation.TruePositive + Evaluation.FalseNegative}）";

    public string FalseAlarmRateText =>
        Evaluation.PairedCount <= 0
            ? "—"
            : $"{Evaluation.FalseAlarmRate:P1}（FP {Evaluation.FalsePositive}）";

    public string AccuracyText =>
        Evaluation.ClassifiedCount <= 0 ? "—" : $"{Evaluation.ClassificationAccuracy:P1}";

    public string ConfusionText =>
        Evaluation.PairedCount <= 0
            ? "—"
            : $"TP {Evaluation.TruePositive} · TN {Evaluation.TrueNegative} · FP {Evaluation.FalsePositive} · FN {Evaluation.FalseNegative}";

    public string FullReportText
    {
        get
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(Headline);
            sb.AppendLine($"生成时间：{GeneratedAtText}");
            sb.AppendLine($"模型：{ModelText}");
            sb.AppendLine($"设备：{DeviceText}");
            sb.AppendLine();
            sb.AppendLine("【运行概况】");
            sb.AppendLine($"总数 {TotalCount} · 成功 {SuccessCount} · 失败 {FailedCount} · 成功率 {SuccessRateText}");
            sb.AppendLine($"总耗时 {TotalWallText} · 成功均耗时 {AverageSuccessText} · 范围 {MinMaxSuccessText}");
            sb.AppendLine();
            sb.AppendLine("【分类评估】");
            sb.AppendLine($"成对样本 {Evaluation.ClassifiedCount} · 准确率 {AccuracyText}");
            sb.AppendLine("【OK/NG 子集】（NG 为正类：漏检=预计NG且推理OK）");
            sb.AppendLine($"成对样本 {Evaluation.PairedCount}");
            sb.AppendLine($"漏检率 {MissRateText}");
            sb.AppendLine($"误报率 {FalseAlarmRateText}");
            sb.AppendLine($"混淆 {ConfusionText}");
            sb.AppendLine();
            sb.AppendLine("【预测异常】（分类错误 / 漏检 / 误报）");
            if (AnomalyCount <= 0)
            {
                sb.AppendLine("无");
            }
            else
            {
                sb.AppendLine(AnomalySummaryText);
                sb.AppendLine("文件\t来源\t预计\t推理\t评估\t耗时\t状态");
                foreach (var row in AnomalySamples)
                {
                    sb.AppendLine(
                        $"{row.DisplayName}\t{row.SourceLabel}\t{row.ExpectedText}\t{row.PredictedText}\t{row.EvaluationTag}\t{row.ElapsedText}\t{row.StatusText}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("【逐样本】");
            sb.AppendLine("文件\t来源\t预计\t推理\t评估\t耗时\t状态");
            foreach (var row in Samples)
            {
                sb.AppendLine(
                    $"{row.DisplayName}\t{row.SourceLabel}\t{row.ExpectedText}\t{row.PredictedText}\t{row.EvaluationTag}\t{row.ElapsedText}\t{row.StatusText}");
            }

            return sb.ToString();
        }
    }
}

public sealed record InferenceOkNgEvaluation(
    int PairedCount,
    int TruePositive,
    int TrueNegative,
    int FalsePositive,
    int FalseNegative)
{
    public int ClassifiedCount { get; init; } = PairedCount;
    public int CorrectClassCount { get; init; } = TruePositive + TrueNegative;
    public double ClassificationAccuracy => ClassifiedCount <= 0 ? 0 : (double)CorrectClassCount / ClassifiedCount;
    public double MissRate =>
        TruePositive + FalseNegative <= 0
            ? 0
            : (double)FalseNegative / (TruePositive + FalseNegative);

    public double FalseAlarmRate =>
        FalsePositive + TrueNegative <= 0
            ? 0
            : (double)FalsePositive / (FalsePositive + TrueNegative);

    public double Accuracy =>
        PairedCount <= 0
            ? 0
            : (double)(TruePositive + TrueNegative) / PairedCount;

    public string SummaryText
    {
        get
        {
            if (ClassifiedCount <= 0)
            {
                return "评估 —（需预计+推理标记）";
            }

            var summary = $"分类评估 {ClassifiedCount} · 准确率 {ClassificationAccuracy:P1}";
            return PairedCount <= 0 ? summary
                : $"{summary} · OK/NG {PairedCount} · 漏检 {MissRate:P1}（FN {FalseNegative}）· 误报 {FalseAlarmRate:P1}";
        }
    }

    public static InferenceOkNgEvaluation FromItems(IEnumerable<InferenceImageItem> items)
    {
        var tp = 0;
        var tn = 0;
        var fp = 0;
        var fn = 0;
        var paired = 0;
        var classified = 0;
        var correct = 0;
        foreach (var item in items)
        {
            if (!item.HasExpected || !item.HasPredicted || !string.IsNullOrWhiteSpace(item.LastInferError))
            {
                continue;
            }

            classified++;
            if (string.Equals(item.ExpectedClass!.Trim(), item.PredictedClass!.Trim(), StringComparison.OrdinalIgnoreCase)) correct++;
            if (!(InferenceImageItem.IsOk(item.ExpectedClass) || InferenceImageItem.IsNg(item.ExpectedClass))
                || !(InferenceImageItem.IsOk(item.PredictedClass) || InferenceImageItem.IsNg(item.PredictedClass))) continue;
            paired++;
            var expNg = InferenceImageItem.IsNg(item.ExpectedClass);
            var predNg = InferenceImageItem.IsNg(item.PredictedClass);
            if (expNg && predNg)
            {
                tp++;
            }
            else if (!expNg && !predNg)
            {
                tn++;
            }
            else if (!expNg && predNg)
            {
                fp++;
            }
            else
            {
                fn++;
            }
        }

        return new InferenceOkNgEvaluation(paired, tp, tn, fp, fn) { ClassifiedCount = classified, CorrectClassCount = correct };
    }
}

public static class InferenceOkNgPrediction
{
    /// <summary>
    /// Map raw inference output to image-level OK/NG when possible.
    /// </summary>
    public static (string? Class, double? Confidence) TryResolve(
        InferenceResult result,
        string modelType)
    {
        if (!result.Success)
        {
            return (null, null);
        }

        if (ModelTypeNames.IsClassificationModelType(modelType))
        {
            var top = result.Detections.OrderByDescending(d => d.Confidence).FirstOrDefault();
            if (top is null) return (null, null);
            var name = string.IsNullOrWhiteSpace(top.ClassName) ? top.Label : top.ClassName;
            return (name?.Trim(), top.Confidence);
        }

        return (null, null);
    }
}

public sealed record InferenceResultSummary
{
    public required bool Success { get; init; }

    public string? Error { get; init; }

    public string Headline { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string PerformanceText { get; init; } = string.Empty;

    public IReadOnlyList<InferenceDetectionRow> Rows { get; init; } = Array.Empty<InferenceDetectionRow>();

    public static InferenceResultSummary FromFailure(string error) =>
        new()
        {
            Success = false,
            Error = error,
            Headline = "推理失败",
            Detail = error
        };

    public static InferenceResultSummary FromResult(InferenceResult result, string modelType)
    {
        if (!result.Success)
        {
            return FromFailure(result.Error ?? "推理失败");
        }

        // Prefer staged summary (preflight / load / infer / crops) when performance is present.
        var performance = result.Performance?.GetSummary() ?? string.Empty;

        if (ModelTypeNames.IsClassificationModelType(modelType))
        {
            var top = result.Detections
                .OrderByDescending(item => item.Confidence)
                .FirstOrDefault();
            if (top is null)
            {
                return new InferenceResultSummary
                {
                    Success = true,
                    Headline = "无分类结果",
                    Detail = "模型未返回类别分数。",
                    PerformanceText = performance
                };
            }

            var className = string.IsNullOrWhiteSpace(top.ClassName) ? top.Label : top.ClassName;
            return new InferenceResultSummary
            {
                Success = true,
                Headline = $"{className}（{top.Confidence:P1}）",
                Detail = "\u5206\u7c7b top-1",
                PerformanceText = performance,
                Rows =
                [
                    new InferenceDetectionRow
                    {
                        ClassName = className,
                        Confidence = top.Confidence
                    }
                ]
            };
        }



        return new InferenceResultSummary
        {
            Success = true,
            Headline = "无分类结果",
            Detail = ModelTypeNames.GetDisplayLabel(modelType),
            PerformanceText = performance
        };
    }
}

public sealed record InferenceDeviceOption(string DisplayName, string DeviceValue)
{
    public static InferenceDeviceOption Cpu { get; } = new("CPU", "cpu");

    public static InferenceDeviceOption PrimaryGpu(string displayName = "GPU (cuda:0)") =>
        new(displayName, "cuda:0");

    public static InferenceDeviceOption GpuIndex(int index, string displayName) =>
        new(displayName, $"cuda:{index}");
}

