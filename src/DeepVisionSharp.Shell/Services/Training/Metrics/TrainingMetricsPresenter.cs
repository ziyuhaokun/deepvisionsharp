using CommunityToolkit.Mvvm.ComponentModel;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.Services.Training.Metrics;

public enum MetricValueFormat
{
    Percent,
    Loss,
    Raw
}

public sealed record MetricCardTemplate(
    string Key,
    string Title,
    int Order,
    MetricValueFormat Format,
    Func<TrainingMetricPoint, double?> SelectValue,
    bool PreferHigher = true);

public sealed partial class MetricCardViewModel : ObservableObject
{
    public MetricCardViewModel(string key, string title)
    {
        Key = key;
        Title = title;
    }

    public string Key { get; }

    public string Title { get; }

    [ObservableProperty]
    private string _valueText = "—";

    [ObservableProperty]
    private string _deltaText = string.Empty;

    [ObservableProperty]
    private string _bestEpochText = string.Empty;

    [ObservableProperty]
    private string _footnote = string.Empty;

    public bool HasFootnote => !string.IsNullOrWhiteSpace(Footnote);

    partial void OnFootnoteChanged(string value) => OnPropertyChanged(nameof(HasFootnote));
}

public sealed record TrainingChartSeriesDefinition(
    string Name,
    Func<TrainingMetricPoint, double?> SelectValue);

public interface ITrainingMetricsPresenter
{
    MetricProfileKind Profile { get; }

    string QualityChartTitle { get; }

    string QualityChartEmptyText { get; }

    string ResultsBestMetricLabel { get; }

    string CurrentEpochTitleFormat { get; }

    string? MetricsFooterHint { get; }

    bool ShowsSecondaryTrainLossRow { get; }

    bool ShowsLearningRateChartColumn { get; }

    IReadOnlyList<MetricCardTemplate> CardTemplates { get; }

    IReadOnlyList<TrainingChartSeriesDefinition> QualitySeries { get; }

    IReadOnlyList<TrainingChartSeriesDefinition> LossSeries { get; }

    IReadOnlyList<TrainingChartSeriesDefinition> LrSeries { get; }

    Func<TrainingMetricPoint, double?> PrimaryQualitySelector { get; }

    void ApplyHistory(
        IReadOnlyList<TrainingMetricPoint> history,
        IList<MetricCardViewModel> cards,
        out string currentEpochText,
        out string trainLossText,
        out string trainLossDeltaText,
        out string lrText,
        out IReadOnlyList<TrainingChartSeries> qualitySeries,
        out IReadOnlyList<TrainingChartSeries> lossSeries,
        out IReadOnlyList<TrainingChartSeries> lrSeries,
        out double? bestPrimaryQuality,
        out int? bestPrimaryEpoch);
}

public static class TrainingMetricsPresenterCatalog
{
    public static ITrainingMetricsPresenter Resolve(MetricProfileKind profile) => profile switch
    {
        MetricProfileKind.ClassificationAccuracy => Classification,
        MetricProfileKind.DetectionMap => Detection,
        _ => None
    };

    public static ITrainingMetricsPresenter ForModelType(string? modelType) =>
        Resolve(MetricProfileCatalog.ForModelType(modelType));

    private static readonly ITrainingMetricsPresenter None = new FixedMetricsPresenter(
        MetricProfileKind.None,
        "训练指标",
        "当前任务无验证指标曲线",
        "训练指标：",
        "当前指标",
        null,
        false,
        false,
        [],
        [],
        [],
        [],
        _ => null);

    private static readonly ITrainingMetricsPresenter Classification = new FixedMetricsPresenter(
        MetricProfileKind.ClassificationAccuracy,
        "分类准确率",
        "等待 Top-1 验证指标",
        "最佳 Top-1：",
        "当前分类指标（第 {0} 轮）",
        "Top-1 为验证集整图分类正确率。最佳权重按 Top-1 最高轮次选取。",
        false,
        true,
        [
            new("top1", "Top-1", 0, MetricValueFormat.Percent, p => p.AccuracyTop1 ?? p.ValMap50),
            new("top5", "Top-5", 1, MetricValueFormat.Percent, p => p.AccuracyTop5),
            new("train_loss", "训练损失", 2, MetricValueFormat.Loss, p => p.TrainLoss, PreferHigher: false),
            new("val_loss", "验证损失", 3, MetricValueFormat.Loss, p => p.ValLoss, PreferHigher: false)
        ],
        [
            new("Top-1", p => p.AccuracyTop1 ?? p.ValMap50),
            new("Top-5", p => p.AccuracyTop5)
        ],
        [
            new("Train loss", p => p.TrainLoss),
            new("Val loss", p => p.ValLoss)
        ],
        [new("LR", p => p.Lr)],
        p => p.AccuracyTop1 ?? p.ValMap50);

    private static readonly ITrainingMetricsPresenter Detection = new FixedMetricsPresenter(
        MetricProfileKind.DetectionMap,
        "检测 mAP",
        "等待 mAP50 验证指标",
        "最佳 mAP50：",
        "当前检测指标（第 {0} 轮）",
        "mAP 来自验证集框匹配（IoU 0.5–0.95）；P/R/F1 按置信度 0.5、IoU 0.5 统计。",
        false,
        true,
        [
            new("map50", "mAP50", 0, MetricValueFormat.Percent, p => p.BoxMap50 ?? p.ValMap50),
            new("map", "mAP50-95", 1, MetricValueFormat.Percent, p => p.BoxMap ?? p.ValMap),
            new("train_loss", "训练损失", 2, MetricValueFormat.Loss, p => p.TrainLoss, PreferHigher: false),
            new("val_loss", "验证损失", 3, MetricValueFormat.Loss, p => p.ValLoss, PreferHigher: false)
        ],
        [
            new("mAP50", p => p.BoxMap50 ?? p.ValMap50),
            new("mAP50-95", p => p.BoxMap ?? p.ValMap)
        ],
        [
            new("Train loss", p => p.TrainLoss),
            new("Val loss", p => p.ValLoss)
        ],
        [new("LR", p => p.Lr)],
        p => p.BoxMap50 ?? p.ValMap50);
}

internal sealed class FixedMetricsPresenter : ITrainingMetricsPresenter
{
    public FixedMetricsPresenter(
        MetricProfileKind profile,
        string qualityChartTitle,
        string qualityChartEmptyText,
        string resultsBestMetricLabel,
        string currentEpochTitleFormat,
        string? metricsFooterHint,
        bool showsSecondaryTrainLossRow,
        bool showsLearningRateChartColumn,
        IReadOnlyList<MetricCardTemplate> cardTemplates,
        IReadOnlyList<TrainingChartSeriesDefinition> qualitySeries,
        IReadOnlyList<TrainingChartSeriesDefinition> lossSeries,
        IReadOnlyList<TrainingChartSeriesDefinition> lrSeries,
        Func<TrainingMetricPoint, double?> primaryQualitySelector)
    {
        Profile = profile;
        QualityChartTitle = qualityChartTitle;
        QualityChartEmptyText = qualityChartEmptyText;
        ResultsBestMetricLabel = resultsBestMetricLabel;
        CurrentEpochTitleFormat = currentEpochTitleFormat;
        MetricsFooterHint = metricsFooterHint;
        ShowsSecondaryTrainLossRow = showsSecondaryTrainLossRow;
        ShowsLearningRateChartColumn = showsLearningRateChartColumn;
        CardTemplates = cardTemplates;
        QualitySeries = qualitySeries;
        LossSeries = lossSeries;
        LrSeries = lrSeries;
        PrimaryQualitySelector = primaryQualitySelector;
    }

    public MetricProfileKind Profile { get; }
    public string QualityChartTitle { get; }
    public string QualityChartEmptyText { get; }
    public string ResultsBestMetricLabel { get; }
    public string CurrentEpochTitleFormat { get; }
    public string? MetricsFooterHint { get; }
    public bool ShowsSecondaryTrainLossRow { get; }
    public bool ShowsLearningRateChartColumn { get; }
    public IReadOnlyList<MetricCardTemplate> CardTemplates { get; }
    public IReadOnlyList<TrainingChartSeriesDefinition> QualitySeries { get; }
    public IReadOnlyList<TrainingChartSeriesDefinition> LossSeries { get; }
    public IReadOnlyList<TrainingChartSeriesDefinition> LrSeries { get; }
    public Func<TrainingMetricPoint, double?> PrimaryQualitySelector { get; }

    public void ApplyHistory(
        IReadOnlyList<TrainingMetricPoint> history,
        IList<MetricCardViewModel> cards,
        out string currentEpochText,
        out string trainLossText,
        out string trainLossDeltaText,
        out string lrText,
        out IReadOnlyList<TrainingChartSeries> qualitySeries,
        out IReadOnlyList<TrainingChartSeries> lossSeries,
        out IReadOnlyList<TrainingChartSeries> lrSeries,
        out double? bestPrimaryQuality,
        out int? bestPrimaryEpoch)
    {
        EnsureCards(cards);

        if (history.Count == 0)
        {
            currentEpochText = "指标尚未就绪";
            trainLossText = "等待当前轮次指标";
            trainLossDeltaText = string.Empty;
            lrText = "—";
            foreach (var card in cards)
            {
                card.ValueText = "等待当前轮次指标";
                card.DeltaText = string.Empty;
                card.BestEpochText = string.Empty;
            }

            qualitySeries = [];
            lossSeries = [];
            lrSeries = [];
            bestPrimaryQuality = null;
            bestPrimaryEpoch = null;
            return;
        }

        var ordered = history.OrderBy(point => point.Epoch).ToList();
        var last = ordered[^1];

        currentEpochText = string.Format(CurrentEpochTitleFormat, last.Epoch);
        trainLossText = FormatValue(last.TrainLoss, MetricValueFormat.Loss);
        var previousEpoch = ordered.Count > 1 ? ordered[^2] : null;
        trainLossDeltaText = FormatDelta(
            last.TrainLoss,
            previousEpoch?.TrainLoss,
            previousEpoch is null,
            MetricValueFormat.Loss,
            preferHigher: false);
        lrText = last.Lr is double lr && double.IsFinite(lr) ? lr.ToString("0.######") : "当前轮次无学习率";

        foreach (var template in CardTemplates.OrderBy(item => item.Order))
        {
            var card = cards.First(item => item.Key == template.Key);
            var latestPoint = FindLatestPointWithValue(ordered, template.SelectValue, last.Epoch);
            var current = latestPoint is null ? null : template.SelectValue(latestPoint);
            var previousPoint = latestPoint is null
                ? null
                : FindLatestPointWithValue(ordered, template.SelectValue, latestPoint.Epoch - 1);
            var prev = previousPoint is null ? null : template.SelectValue(previousPoint);
            card.ValueText = FormatValue(current, template.Format);
            card.DeltaText = FormatDelta(
                current,
                prev,
                previousPoint is null,
                template.Format,
                template.PreferHigher);

            var bestPoint = template.PreferHigher
                ? ordered.Where(p => template.SelectValue(p).HasValue).MaxBy(p => template.SelectValue(p))
                : ordered.Where(p => template.SelectValue(p).HasValue).MinBy(p => template.SelectValue(p));
            if (bestPoint is null)
            {
                card.BestEpochText = "尚无最佳记录";
            }
            else
            {
                var bestValue = template.SelectValue(bestPoint);
                card.BestEpochText =
                    $"最佳 {FormatValue(bestValue, template.Format)} · 第 {bestPoint.Epoch} 轮";
            }

            card.Footnote = template.Key == "top5" ? "二分类时参考意义有限" : string.Empty;
            if (latestPoint is not null && latestPoint.Epoch != last.Epoch)
            {
                var carryNote = $"最近验证 Epoch {latestPoint.Epoch}";
                card.Footnote = string.IsNullOrWhiteSpace(card.Footnote)
                    ? carryNote
                    : $"{card.Footnote}；{carryNote}";
            }
        }

        qualitySeries = BuildSeries(ordered, QualitySeries);
        lossSeries = BuildSeries(ordered, SimpleLossSeries());
        lrSeries = BuildSeries(ordered, LrSeries);

        var bestPrimary = ordered
            .Where(p => PrimaryQualitySelector(p).HasValue)
            .MaxBy(p => PrimaryQualitySelector(p));
        bestPrimaryQuality = bestPrimary is null ? null : PrimaryQualitySelector(bestPrimary);
        bestPrimaryEpoch = bestPrimary?.Epoch;
    }

    private void EnsureCards(IList<MetricCardViewModel> cards)
    {
        var expectedKeys = CardTemplates.OrderBy(t => t.Order).Select(t => t.Key).ToArray();
        var currentKeys = cards.Select(c => c.Key).ToArray();
        if (expectedKeys.SequenceEqual(currentKeys))
        {
            return;
        }

        cards.Clear();
        foreach (var template in CardTemplates.OrderBy(t => t.Order))
        {
            var card = new MetricCardViewModel(template.Key, template.Title);
            if (template.Key == "top5")
            {
                card.Footnote = "二分类时参考意义有限";
            }

            cards.Add(card);
        }
    }

    private static IReadOnlyList<TrainingChartSeriesDefinition> SimpleLossSeries() =>
    [
        new("Train loss", p => p.TrainLoss),
        new("Val loss", p => p.ValLoss)
    ];

    private static IReadOnlyList<TrainingChartSeries> BuildSeries(
        IReadOnlyList<TrainingMetricPoint> metrics,
        IReadOnlyList<TrainingChartSeriesDefinition> definitions)
    {
        var series = new List<TrainingChartSeries>(definitions.Count);
        foreach (var definition in definitions)
        {
            var points = metrics
                .OrderBy(metric => metric.Epoch)
                .Select(metric => (metric.Epoch, Value: definition.SelectValue(metric)))
                .Where(item => item.Value.HasValue && double.IsFinite(item.Value.Value))
                .Select(item => new TrainingChartPoint(item.Epoch, item.Value!.Value))
                .ToList();

            if (points.Count > 0)
            {
                series.Add(new TrainingChartSeries(definition.Name, points));
            }
        }

        return series;
    }

    private static TrainingMetricPoint? FindLatestPointWithValue(
        IReadOnlyList<TrainingMetricPoint> ordered,
        Func<TrainingMetricPoint, double?> selector,
        int maxEpochInclusive)
    {
        for (var index = ordered.Count - 1; index >= 0; index--)
        {
            var point = ordered[index];
            if (point.Epoch > maxEpochInclusive)
            {
                continue;
            }

            var value = selector(point);
            if (value is double number && double.IsFinite(number))
            {
                return point;
            }
        }

        return null;
    }

    private static string FormatValue(double? value, MetricValueFormat format)
    {
        if (value is not double number || !double.IsFinite(number))
        {
            return "当前轮次无数据";
        }

        return format switch
        {
            MetricValueFormat.Percent => $"{number * 100:0.##}%",
            MetricValueFormat.Loss => number.ToString("F4"),
            _ => number.ToString("0.######")
        };
    }

    private static string FormatDelta(
        double? current,
        double? previous,
        bool isFirstEpoch,
        MetricValueFormat format,
        bool preferHigher)
    {
        if (isFirstEpoch)
        {
            return "首轮无可比";
        }

        if (current is not double currentValue || !double.IsFinite(currentValue))
        {
            return "当前轮次无数据";
        }

        if (previous is not double previousValue || !double.IsFinite(previousValue))
        {
            return "上一轮无数据";
        }

        if (format == MetricValueFormat.Percent)
        {
            var deltaPercent = (currentValue - previousValue) * 100;
            if (Math.Abs(deltaPercent) < 0.005)
            {
                return "较上一轮 持平";
            }

            return $"较上一轮 {(deltaPercent >= 0 ? "+" : string.Empty)}{deltaPercent:0.##}%";
        }

        var delta = currentValue - previousValue;
        if (Math.Abs(delta) < 0.00005)
        {
            return "较上一轮 持平";
        }

        return $"较上一轮 {(delta >= 0 ? "+" : string.Empty)}{delta:F4}";
    }
}
