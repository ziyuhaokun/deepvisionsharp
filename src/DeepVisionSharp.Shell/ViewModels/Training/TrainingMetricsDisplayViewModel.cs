using CommunityToolkit.Mvvm.ComponentModel;

using System.Collections.ObjectModel;
using System.IO;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Training.Metrics;
using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.ViewModels.Training;

/// <summary>
/// Metrics tab display state + history/charts for the training run panel.
/// Owned by <see cref="TrainingMonitorViewModel"/>; polling lifecycle is coordinated by the parent.
/// </summary>
public sealed partial class TrainingMetricsDisplayViewModel : ObservableObject
{
    private readonly TrainingJobsViewModel _jobs;
    private readonly TrainingMetricsPoller _poller;
    private readonly IUiDispatcher _uiDispatcher;
    private Func<bool>? _isMetricsSectionActive;

    public TrainingMetricsDisplayViewModel(
        TrainingJobsViewModel jobs,
        TrainingMetricsPoller poller,
        IUiDispatcher uiDispatcher)
    {
        _jobs = jobs;
        _poller = poller;
        _uiDispatcher = uiDispatcher;
    }

    public ObservableCollection<TrainingMetricHistoryRow> MetricHistory { get; } = new();

    /// <summary>Per-class precision/recall/F1 of the latest epoch that reported them.</summary>
    public ObservableCollection<TrainingPerClassRow> PerClassRows { get; } = new();

    public bool HasPerClassRows => PerClassRows.Count > 0;

    public ObservableCollection<MetricCardViewModel> MetricCards { get; } = new();

    [ObservableProperty]
    private IReadOnlyList<TrainingChartSeries> _qualityChartSeries = [];

    [ObservableProperty]
    private IReadOnlyList<TrainingChartSeries> _lossChartSeries = [];

    [ObservableProperty]
    private IReadOnlyList<TrainingChartSeries> _lrChartSeries = [];

    [ObservableProperty]
    private bool _hasTrainingMetrics;

    [ObservableProperty]
    private string _currentMetricEpochText = "指标尚未就绪";

    [ObservableProperty]
    private string _currentTrainLossText = "等待当前轮次指标";

    [ObservableProperty]
    private string _currentLrText = "等待当前轮次指标";

    [ObservableProperty]
    private string _trainLossDeltaText = string.Empty;

    [ObservableProperty]
    private string? _metricsPollingError;

    public bool HasMetricsPollingError => !string.IsNullOrWhiteSpace(MetricsPollingError);

    public MetricProfileKind CurrentMetricProfile =>
        MetricProfileCatalog.ForModelType(_jobs.SelectedJob?.ModelType);

    public ITrainingMetricsPresenter CurrentPresenter =>
        TrainingMetricsPresenterCatalog.ForModelType(_jobs.SelectedJob?.ModelType);

    public bool ShowMetricsContent =>
        HasTrainingMetrics && CurrentMetricProfile is not MetricProfileKind.None;

    public bool ShowMetricCards =>
        ShowMetricsContent && MetricCards.Count > 0;

    public bool ShowClassificationMetricsCards =>
        ShowMetricsContent && CurrentMetricProfile == MetricProfileKind.ClassificationAccuracy;

    public bool ShowMetricsEmptyState =>
        !HasTrainingMetrics && CurrentMetricProfile is not MetricProfileKind.None;

    public string QualityChartTitle => CurrentPresenter.QualityChartTitle;

    public string QualityChartEmptyText => CurrentPresenter.QualityChartEmptyText;

    public string? MetricsFooterHint => CurrentPresenter.MetricsFooterHint;

    public bool ShowMetricsFooterHint => !string.IsNullOrWhiteSpace(MetricsFooterHint);

    public bool ShowSecondaryTrainLossRow =>
        ShowMetricsContent && CurrentPresenter.ShowsSecondaryTrainLossRow;

    public bool ShowLearningRateChartColumn =>
        ShowMetricsContent && CurrentPresenter.ShowsLearningRateChartColumn;

    public void AttachSectionGate(Func<bool> isMetricsSectionActive) =>
        _isMetricsSectionActive = isMetricsSectionActive;

    public void ClearJobMetrics(Guid jobId) => _poller.ClearMetrics(jobId);

    public bool TryParseLiveMetric(string line, Guid jobId) =>
        _poller.TryParseLiveMetric(line, jobId, out _);

    public List<TrainingMetricPoint> GetSelectedJobMetrics()
    {
        if (_jobs.SelectedJob is null)
        {
            return [];
        }

        return _poller.GetMetrics(_jobs.SelectedJob.Id).ToList();
    }

    /// <summary>
    /// Refreshes cards/history/charts from the metrics poller buffer.
    /// Returns true when the active profile is classification (parent may refresh CLS plots).
    /// </summary>
    public bool RefreshDisplay()
    {
        var metrics = GetSelectedJobMetrics().ToList();
        if (CurrentMetricProfile == MetricProfileKind.ClassificationAccuracy)
        {
            ClassificationResultsCsvImporter.MergeInto(_jobs.SelectedJob?.OutputRoot, metrics);
        }

        metrics = metrics.OrderBy(point => point.Epoch).ToList();
        HasTrainingMetrics = metrics.Count > 0;
        MetricHistory.Clear();
        PerClassRows.Clear();

        var presenter = CurrentPresenter;
        presenter.ApplyHistory(
            metrics,
            MetricCards,
            out var epochText,
            out var trainLossText,
            out var trainLossDelta,
            out var lrText,
            out var qualitySeries,
            out var lossSeries,
            out var lrSeries,
            out _,
            out var bestPrimaryEpoch);

        if (metrics.Count == 0)
        {
            var emptyReason = ResolveMetricsEmptyReason();
            CurrentMetricEpochText = emptyReason;
            CurrentTrainLossText = emptyReason;
            CurrentLrText = emptyReason;
            TrainLossDeltaText = string.Empty;
            foreach (var card in MetricCards)
            {
                card.ValueText = emptyReason;
                card.DeltaText = string.Empty;
                card.BestEpochText = string.Empty;
            }

            QualityChartSeries = [];
            LossChartSeries = [];
            LrChartSeries = [];
            OnPropertyChanged(nameof(ShowMetricCards));
            OnPropertyChanged(nameof(HasPerClassRows));
            return CurrentMetricProfile == MetricProfileKind.ClassificationAccuracy;
        }

        CurrentMetricEpochText = epochText;
        CurrentTrainLossText = trainLossText;
        TrainLossDeltaText = trainLossDelta;
        CurrentLrText = lrText;
        QualityChartSeries = qualitySeries;
        LossChartSeries = lossSeries;
        LrChartSeries = lrSeries;
        OnPropertyChanged(nameof(ShowMetricCards));

        var isInstanceSeg = false;
        foreach (var row in metrics
                     .OrderByDescending(point => point.Epoch)
                     .Take(20)
                     .Select(point =>
                     {
                         var top1 = point.AccuracyTop1 ?? point.ValMap50;
                         var maskMap = point.MaskMap ?? point.ValMap;
                         var maskMap50 = point.MaskMap50 ?? point.ValMap50;
                         return new TrainingMetricHistoryRow
                         {
                             Epoch = point.Epoch,
                             TrainLossText = point.TrainLoss?.ToString("F4") ?? "—",
                             ValLossText = point.ValLoss?.ToString("F4") ?? "—",
                             ValMapText = point.ValMap is double map ? FormatPercent(map) : "—",
                             ValMap50Text = point.ValMap50 is double map50 ? FormatPercent(map50) : "—",
                             MaskMapText = maskMap is double mm ? FormatPercent(mm) : "—",
                             MaskMap50Text = maskMap50 is double mm50 ? FormatPercent(mm50) : "—",
                             BoxMapText = point.BoxMap is double bm ? FormatPercent(bm) : "—",
                             BoxMap50Text = point.BoxMap50 is double bm50 ? FormatPercent(bm50) : "—",
                             AccuracyTop1Text = top1 is double t1 ? FormatPercent(t1) : "—",
                             AccuracyTop5Text = point.AccuracyTop5 is double t5 ? FormatPercent(t5) : "—",
                             PrecisionText = isInstanceSeg
                                 ? (point.BoxMap50 is double box50 ? FormatPercent(box50) : "—")
                                 : (point.Precision is double precision ? FormatPercent(precision) : "—"),
                             RecallText = isInstanceSeg
                                 ? (point.BoxMap is double boxMap ? FormatPercent(boxMap) : "—")
                                 : (point.Recall is double recall ? FormatPercent(recall) : "—"),
                             LrText = point.Lr?.ToString("0.######") ?? "—",
                             IsBestMap50 = bestPrimaryEpoch is int epoch && point.Epoch == epoch
                         };
                     }))
        {
            MetricHistory.Add(row);
        }

        var latestPerClass = metrics
            .Where(point => point.PerClass is { Count: > 0 })
            .OrderByDescending(point => point.Epoch)
            .FirstOrDefault();
        if (latestPerClass?.PerClass is { } perClass)
        {
            foreach (var metric in perClass)
            {
                PerClassRows.Add(new TrainingPerClassRow
                {
                    ClassName = metric.ClassName,
                    PrecisionText = FormatPercent(metric.Precision),
                    RecallText = FormatPercent(metric.Recall),
                    F1Text = FormatPercent(metric.F1),
                    SupportText = metric.Support.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            }
        }

        OnPropertyChanged(nameof(HasPerClassRows));
        return CurrentMetricProfile == MetricProfileKind.ClassificationAccuracy;
    }

    public void EnsureMetricsLoadedFromLog(bool fullReload)
    {
        if (_jobs.SelectedJob?.LogFile is not { Length: > 0 } logFile || !File.Exists(logFile))
        {
            return;
        }

        _poller.EnsureMetricsLoaded(_jobs.SelectedJob.Id, logFile, fullReload);
        _ = RefreshDisplay();
    }

    public void StartPolling(bool fullReload)
    {
        MetricsPollingError = null;

        if (_jobs.SelectedJob?.LogFile is not { Length: > 0 } logFile || _jobs.SelectedJob is null)
        {
            _ = RefreshDisplay();
            return;
        }

        EnsureMetricsLoadedFromLog(fullReload: true);
        var jobId = _jobs.SelectedJob.Id;
        _poller.Start(
            jobId,
            logFile,
            () => (_isMetricsSectionActive?.Invoke() ?? false) && _jobs.SelectedJob?.Id == jobId,
            () => _uiDispatcher.InvokeAsync(() =>
            {
                _ = RefreshDisplay();
            }),
            ReportPollingError,
            fullReload);
    }

    public void StopPolling() => _poller.Stop();

    /// <summary>Primary quality text for the results overview card (percent or "—").</summary>
    public bool TryGetBestPrimaryQuality(out string valueText, out string epochText)
    {
        valueText = "—";
        epochText = string.Empty;

        var presenter = CurrentPresenter;
        if (presenter.Profile == MetricProfileKind.None)
        {
            return false;
        }

        var metrics = GetSelectedJobMetrics();
        var best = metrics
            .Where(point => presenter.PrimaryQualitySelector(point).HasValue)
            .MaxBy(point => presenter.PrimaryQualitySelector(point));

        if (best is null || presenter.PrimaryQualitySelector(best) is not double quality)
        {
            return false;
        }

        valueText = FormatPercent(quality);
        epochText = $"第 {best.Epoch} 轮";
        return true;
    }

    public void NotifyJobContextChanged()
    {
        OnPropertyChanged(nameof(CurrentMetricProfile));
        OnPropertyChanged(nameof(ShowMetricsContent));
        OnPropertyChanged(nameof(ShowMetricCards));
        OnPropertyChanged(nameof(ShowClassificationMetricsCards));
        OnPropertyChanged(nameof(ShowSecondaryTrainLossRow));
        OnPropertyChanged(nameof(ShowLearningRateChartColumn));
        OnPropertyChanged(nameof(ShowMetricsFooterHint));
        OnPropertyChanged(nameof(MetricsFooterHint));
        OnPropertyChanged(nameof(ShowMetricsEmptyState));
        OnPropertyChanged(nameof(QualityChartTitle));
        OnPropertyChanged(nameof(QualityChartEmptyText));
    }

    partial void OnMetricsPollingErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasMetricsPollingError));

    partial void OnHasTrainingMetricsChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowMetricsContent));
        OnPropertyChanged(nameof(ShowMetricCards));
        OnPropertyChanged(nameof(ShowClassificationMetricsCards));
        OnPropertyChanged(nameof(ShowSecondaryTrainLossRow));
        OnPropertyChanged(nameof(ShowLearningRateChartColumn));
        OnPropertyChanged(nameof(ShowMetricsFooterHint));
        OnPropertyChanged(nameof(ShowMetricsEmptyState));
    }

    private void ReportPollingError(Exception ex)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            MetricsPollingError = $"指标轮询失败：{ex.Message}";
        });
    }

    private string ResolveMetricsEmptyReason() =>
        _jobs.SelectedJob?.Status switch
        {
            null => "未选择任务",
            TrainingJobStatus.Draft => "尚未开始训练",
            TrainingJobStatus.Preparing => "准备中，尚无指标",
            TrainingJobStatus.Running => "等待当前轮次指标",
            TrainingJobStatus.Cancelled => "任务取消后未记录",
            TrainingJobStatus.Failed => "任务失败，指标不完整",
            TrainingJobStatus.Succeeded => "未记录验证指标",
            _ => "暂无指标数据"
        };

    internal static string FormatPercent(double ratio) => $"{ratio * 100:0.##}%";
}
