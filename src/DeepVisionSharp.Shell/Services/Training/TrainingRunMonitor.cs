using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.IO;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Training.Metrics;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.ViewModels.Training;

namespace DeepVisionSharp.Shell.Services.Training;

/// <summary>
/// Owns the training run lifecycle: log tracking, run-section polling, results/checkpoint/failure
/// presentation, and the ordering of sub-view-model refreshes. The monitor view model keeps only
/// bindable tab state and command shells.
/// </summary>
public sealed partial class TrainingRunMonitor : ObservableObject, IDisposable
{
    private readonly TrainingJobsViewModel _jobs;
    private readonly TrainingLogViewModel _log;
    private readonly TrainingMetricsPoller _metricsPolling;
    private readonly TrainingPreviewPoller _previewPolling;
    private readonly TrainingResourcePoller _resourcePolling;
    private readonly IProcessLauncher _processLauncher;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IClipboardService _clipboard;
    private bool _suppressTerminalStatusToasts;
    private bool _isRunMode;
    private TrainingRunSection _activeSection = TrainingRunSection.Overview;

    public TrainingRunMonitor(
        TrainingJobsViewModel jobs,
        TrainingLogViewModel log,
        TrainingMetricsPoller metricsPoller,
        TrainingPreviewPoller previewPoller,
        TrainingResourcePoller resourcePoller,
        IProcessLauncher processLauncher,
        ShellSnackbarNotifier snackbar,
        IClipboardService clipboard,
        IUiDispatcher uiDispatcher)
    {
        _jobs = jobs;
        _log = log;
        _metricsPolling = metricsPoller;
        _previewPolling = previewPoller;
        _resourcePolling = resourcePoller;
        _processLauncher = processLauncher;
        _snackbar = snackbar;
        _clipboard = clipboard;

        Metrics = new TrainingMetricsDisplayViewModel(jobs, metricsPoller, uiDispatcher);
        Metrics.AttachSectionGate(() => IsMetricsSectionActive);
        Preview = new TrainingPreviewPanelViewModel(
            jobs,
            previewPoller,
            processLauncher,
            snackbar,
            clipboard,
            uiDispatcher);
        Preview.AttachPollingGate(() => IsPreviewSectionActive || IsOverviewSectionActive);
        Resources = new TrainingResourcePanelViewModel(resourcePoller, uiDispatcher);
        Resources.AttachSectionGate(() => IsResourcesSectionActive);

        BestCheckpointCard = new TrainingCheckpointCardViewModel(TrainingCheckpointKind.Best, "最佳模型（验证集最优）");
        LastCheckpointCard = new TrainingCheckpointCardViewModel(TrainingCheckpointKind.Last, "末轮模型（最后一 epoch）");

        _log.LineAppended += OnLogLineAppended;
        _jobs.ActiveJobElapsedTick += OnActiveJobElapsedTick;
    }

    public TrainingMetricsDisplayViewModel Metrics { get; }

    public TrainingPreviewPanelViewModel Preview { get; }

    public TrainingResourcePanelViewModel Resources { get; }

    public TrainingCheckpointCardViewModel BestCheckpointCard { get; }

    public TrainingCheckpointCardViewModel LastCheckpointCard { get; }

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _resultsBestMap50Text = "—";

    [ObservableProperty]
    private string _resultsBestMap50EpochText = string.Empty;

    [ObservableProperty]
    private string _resultsEmptyTitle = "训练进行中";

    [ObservableProperty]
    private string _resultsEmptyDescription = "训练完成后将显示最佳模型";

    [ObservableProperty]
    private bool _showResultsContent;

    [ObservableProperty]
    private bool _showResultsEmptyState = true;

    [ObservableProperty]
    private TrainingCheckpointSelection _selectedCheckpointForSave = TrainingCheckpointSelection.Best;

    public string ResultsBestMetricLabel => Metrics.CurrentPresenter.ResultsBestMetricLabel;

    public bool IsSelectedJobClassificationModel =>
        _jobs.SelectedJob is not null
        && ModelTypeNames.IsClassificationModelType(_jobs.SelectedJob.ModelType);

    public bool ShowDualCheckpointResults =>
        ShowResultsContent
        && _jobs.SelectedJob is not null
        && TrainingJobActionRules.SupportsDualCheckpointUi(_jobs.SelectedJob);

    public bool ShowSingleCheckpointResults =>
        ShowResultsContent && !ShowDualCheckpointResults;

    public bool CanSaveSelectedCheckpoint =>
        _jobs.SelectedJob is not null
        && TrainingJobActionRules.CanSaveCheckpoint(_jobs.SelectedJob, SelectedCheckpointForSave);

    public bool IsBestCheckpointSelected
    {
        get => SelectedCheckpointForSave == TrainingCheckpointSelection.Best;
        set
        {
            if (value)
            {
                SelectedCheckpointForSave = TrainingCheckpointSelection.Best;
            }
        }
    }

    public bool IsLastCheckpointSelected
    {
        get => SelectedCheckpointForSave == TrainingCheckpointSelection.Last;
        set
        {
            if (value)
            {
                SelectedCheckpointForSave = TrainingCheckpointSelection.Last;
            }
        }
    }

    public string SaveSelectedCheckpointToolTip
    {
        get
        {
            if (_jobs.SelectedJob is null || !CanSaveSelectedCheckpoint)
            {
                return "需要训练成功、已取消或失败后有可用权重文件";
            }

            var label = SelectedCheckpointForSave == TrainingCheckpointSelection.Last
                ? "末轮"
                : "最佳";
            return _jobs.SelectedJob.Status == TrainingJobStatus.Cancelled
                ? $"将{label}模型（取消任务中产生的权重）保存到模型库"
                : $"将{label}模型保存到模型库";
        }
    }

    public bool ShowFailureSummary =>
        _jobs.SelectedJob?.Status == TrainingJobStatus.Failed;

    public string FailureSummary => SplitFailure().Summary;

    public string FailureTraceback => SplitFailure().Traceback;

    public bool ShowFailureTraceback => !string.IsNullOrWhiteSpace(FailureTraceback);

    public string TrainingDurationText
    {
        get
        {
            if (_jobs.SelectedJob?.StartedAt is null)
            {
                return "—";
            }

            return TrainingJobTiming.FormatDuration(
                TrainingJobTiming.ComputeElapsed(
                    _jobs.SelectedJob.StartedAt,
                    _jobs.SelectedJob.FinishedAt));
        }
    }

    public string TrainingStartedAtText =>
        TrainingJobTiming.FormatStartedAtLocalFull(_jobs.SelectedJob?.StartedAt);

    public string TrainingFinishedAtText =>
        TrainingJobTiming.FormatFinishedAtLocal(_jobs.SelectedJob?.FinishedAt);

    public bool IsMetricsSectionActive => _isRunMode && _activeSection == TrainingRunSection.Metrics;

    public bool IsOverviewSectionActive => _isRunMode && _activeSection == TrainingRunSection.Overview;

    public bool IsPreviewSectionActive => _isRunMode && _activeSection == TrainingRunSection.Preview;

    public bool IsResourcesSectionActive => _isRunMode && _activeSection == TrainingRunSection.Resources;

    /// <summary>Called whenever the shell changes detail mode or run section.</summary>
    public void UpdateActiveSection(bool isRunMode, TrainingRunSection section)
    {
        _isRunMode = isRunMode;
        _activeSection = section;
    }

    /// <summary>Starts/stops sub-view-model polling to match the active run section.</summary>
    public void ApplySectionSideEffects()
    {
        StopAllTabPolling();

        switch (_activeSection)
        {
            case TrainingRunSection.Metrics:
                StartMetricsPolling(fullReload: true);
                break;
            case TrainingRunSection.Preview:
            case TrainingRunSection.Overview:
                Preview.StartPolling();
                RefreshResultsState();
                break;
            case TrainingRunSection.Resources:
                Resources.StartPolling();
                break;
            case TrainingRunSection.Logs:
                _log.TailCurrentLogOnDemand();
                _log.FlushToUi(force: true);
                break;
        }
    }

    public void HandleNoJob()
    {
        _log.StopTracking();
        StopAllTabPolling();
        NotifySelectedJobMetricContext();
        Metrics.RefreshDisplay();
        Preview.RefreshPreviewImages(force: true);
        Preview.ClearTrainingPlotImages();
        RefreshResultsState();
    }

    public void HandleSelectedJobChanged(TrainingJobListItem job)
    {
        StartLogTracking(job.Id, job.LogFile, job);
        RefreshRunningState(job);
        NotifySelectedJobMetricContext();

        if (job.Status != TrainingJobStatus.Draft)
        {
            OnSelectedJobTabRefresh();
            return;
        }

        StopAllTabPolling();
        Metrics.RefreshDisplay();
        Preview.RefreshPreviewImages(force: true);
        Preview.ClearTrainingPlotImages();
        RefreshResultsState();
        NotifyTiming();
    }

    public void UpdateRunningStateForStatus(Guid jobId, TrainingJobStatus status)
    {
        if (!_log.IsTracking(jobId))
        {
            return;
        }

        IsRunning = TrainingJobTransitions.IsActive(status);
        if (!TrainingJobTransitions.IsActive(status))
        {
            _log.UpdateRunningState(isRunning: false);
            _log.TailCurrentLogOnDemand();
            _log.FlushToUi(force: true);
        }
    }

    public void RefreshResultsAndMetricsAfterStatus()
    {
        RefreshResultsState();
        EnsureMetricsLoadedFromLog(fullReload: true);
    }

    public void ShowTerminalStatusToast(Guid jobId, TrainingJobStatus status, string? bestModelPath)
    {
        if (!_log.IsTracking(jobId) || _suppressTerminalStatusToasts)
        {
            return;
        }

        switch (status)
        {
            case TrainingJobStatus.Succeeded:
                _snackbar.ShowSuccess(
                    bestModelPath is null ? "训练完成。" : $"训练完成，模型：{bestModelPath}",
                    "训练");
                break;
            case TrainingJobStatus.Failed:
                _snackbar.ShowError("训练失败，请查看日志。", "训练");
                break;
            case TrainingJobStatus.Cancelled:
                _snackbar.ShowInfo("训练已取消。", "训练");
                break;
        }
    }

    public void OnSelectedJobTabRefresh()
    {
        NotifyFailurePresentation();
        RefreshResultsState();
        EnsureMetricsLoadedFromLog(fullReload: true);
        Preview.RefreshPreviewImages(force: true);
        if (_isRunMode)
        {
            ApplySectionSideEffects();
        }

        NotifyTiming();
    }

    public void StopAllTabPolling()
    {
        Metrics.StopPolling();
        Preview.StopPolling();
        Resources.StopPolling();
    }

    public void StartLogTracking(Guid jobId, string? logFile, TrainingJobListItem? job)
    {
        _log.StartTracking(jobId, logFile, TrainingJobUi.IsActiveStatus(job?.Status ?? TrainingJobStatus.Draft));
        RefreshRunningState(job);
    }

    public void RefreshRunningState(TrainingJobListItem? job)
    {
        if (job is null)
        {
            IsRunning = false;
            _log.UpdateRunningState(isRunning: false);
            return;
        }

        IsRunning = TrainingJobUi.IsActiveStatus(job.Status);
        _log.UpdateRunningState(IsRunning);
    }

    public void AppendLogLine(string line) => _log.AppendLogLine(line);

    public void ClearLogContent() => _log.ClearLogContent();

    /// <summary>
    /// Clears live log and suppresses terminal status toasts until <see cref="AllowTerminalStatusToasts"/>.
    /// Draft selection already tracks the job id; without this, a false MarkFailed mid-start toasts "训练失败".
    /// </summary>
    public void ResetLogForStart()
    {
        _suppressTerminalStatusToasts = true;
        _log.ClearLogContent();
    }

    public void AllowTerminalStatusToasts() => _suppressTerminalStatusToasts = false;

    public void ClearJobMetrics(Guid jobId) => Metrics.ClearJobMetrics(jobId);

    public void NotifyFailurePresentation()
    {
        OnPropertyChanged(nameof(ShowFailureSummary));
        OnPropertyChanged(nameof(FailureSummary));
        OnPropertyChanged(nameof(FailureTraceback));
        OnPropertyChanged(nameof(ShowFailureTraceback));
    }

    public void Dispose()
    {
        _jobs.ActiveJobElapsedTick -= OnActiveJobElapsedTick;
        StopAllTabPolling();
        _log.LineAppended -= OnLogLineAppended;
        _metricsPolling.Dispose();
        _previewPolling.Dispose();
        _resourcePolling.Dispose();
    }

    [RelayCommand]
    private void CopyFailureDetail()
    {
        var raw = _jobs.SelectedJob?.FailureMessage;
        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = FailureSummary;
        }

        try
        {
            _clipboard.SetText(raw.Trim());
            _snackbar.ShowSuccess("失败详情已复制到剪贴板。", "训练");
        }
        catch
        {
            _snackbar.ShowError("复制失败详情失败。", "训练");
        }
    }

    [RelayCommand]
    private void OpenSelectedCheckpointFolder()
    {
        if (_jobs.SelectedJob is null)
        {
            return;
        }

        var path = TrainingJobActionRules.ResolveCheckpointPath(
            _jobs.SelectedJob,
            SelectedCheckpointForSave);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = _jobs.SelectedJob.BestModelPath ?? _jobs.SelectedJob.LastModelPath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var folder = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrWhiteSpace(folder) || !_processLauncher.TryOpenFolder(folder))
        {
            _snackbar.ShowWarning("模型目录不存在。", "训练");
        }
    }

    [RelayCommand]
    private void SelectBestCheckpointForSave()
    {
        SelectedCheckpointForSave = TrainingCheckpointSelection.Best;
    }

    [RelayCommand]
    private void SelectLastCheckpointForSave()
    {
        SelectedCheckpointForSave = TrainingCheckpointSelection.Last;
    }

    partial void OnIsRunningChanged(bool value) => NotifyTiming();

    partial void OnSelectedCheckpointForSaveChanged(TrainingCheckpointSelection value)
    {
        OnPropertyChanged(nameof(IsBestCheckpointSelected));
        OnPropertyChanged(nameof(IsLastCheckpointSelected));
        OnPropertyChanged(nameof(CanSaveSelectedCheckpoint));
        OnPropertyChanged(nameof(SaveSelectedCheckpointToolTip));
    }

    partial void OnShowResultsContentChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDualCheckpointResults));
        OnPropertyChanged(nameof(ShowSingleCheckpointResults));
    }

    private void OnLogLineAppended(string line)
    {
        if (_jobs.SelectedJob?.Id is not Guid jobId || !Metrics.TryParseLiveMetric(line, jobId))
        {
            return;
        }

        if (IsMetricsSectionActive && _jobs.SelectedJob?.Id == jobId)
        {
            RefreshMetricsDisplay();
        }

        if (IsOverviewSectionActive && _jobs.SelectedJob?.Id == jobId)
        {
            UpdateResultsBestMetric();
        }
    }

    private void OnActiveJobElapsedTick()
    {
        if (_jobs.SelectedJob is null
            || !TrainingJobUi.IsActiveStatus(_jobs.SelectedJob.Status)
            || _jobs.SelectedJob.StartedAt is null)
        {
            return;
        }

        OnPropertyChanged(nameof(TrainingDurationText));
    }

    private void NotifyTiming()
    {
        OnPropertyChanged(nameof(TrainingDurationText));
        OnPropertyChanged(nameof(TrainingStartedAtText));
        OnPropertyChanged(nameof(TrainingFinishedAtText));
    }

    private (string Summary, string Traceback) SplitFailure()
        => SplitFailureMessage(_jobs.SelectedJob?.FailureMessage);

    private static (string Summary, string Traceback) SplitFailureMessage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ("未记录具体失败原因", string.Empty);
        }

        var normalized = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        var gap = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        if (gap >= 0)
        {
            return (normalized[..gap].Trim(), normalized[(gap + 2)..].Trim());
        }

        var firstNl = normalized.IndexOf('\n');
        if (firstNl < 0)
        {
            return (normalized, string.Empty);
        }

        return (normalized[..firstNl].Trim(), normalized[(firstNl + 1)..].Trim());
    }

    private void RefreshResultsState()
    {
        if (_jobs.SelectedJob is null)
        {
            ShowResultsContent = false;
            ShowResultsEmptyState = true;
            ResultsEmptyTitle = "暂无结果";
            ResultsEmptyDescription = "请先选择训练任务";
            NotifyTiming();
            return;
        }

        NotifyTiming();

        var status = _jobs.SelectedJob.Status;
        var hasWeightArtifact = TrainingJobActionRules.HasAnySaveableCheckpoint(_jobs.SelectedJob);

        if ((status is TrainingJobStatus.Succeeded or TrainingJobStatus.Cancelled or TrainingJobStatus.Failed)
            && hasWeightArtifact)
        {
            ShowResultsContent = true;
            ShowResultsEmptyState = false;
            RefreshCheckpointCards();
            UpdateResultsBestMetric();
            Preview.RefreshTrainingPlotImages();
            return;
        }

        // While running, still surface any partial training plots for classification.
        if (IsSelectedJobClassificationModel
            && status is TrainingJobStatus.Running or TrainingJobStatus.Preparing or TrainingJobStatus.Succeeded)
        {
            Preview.RefreshTrainingPlotImages();
        }
        else if (!IsSelectedJobClassificationModel)
        {
            Preview.ClearTrainingPlotImages();
        }

        ShowResultsContent = false;
        ShowResultsEmptyState = true;

        if (status == TrainingJobStatus.Failed)
        {
            ResultsEmptyTitle = "训练失败";
            ResultsEmptyDescription = "请查看运行日志了解详细错误信息";
        }
        else if (status == TrainingJobStatus.Cancelled)
        {
            ResultsEmptyTitle = "训练已中止";
            ResultsEmptyDescription = "未找到可用权重文件，无法保存到模型库";
        }
        else if (status == TrainingJobStatus.Draft)
        {
            ResultsEmptyTitle = "草稿任务";
            ResultsEmptyDescription = "配置参数后点击下方开始训练启动任务";
        }
        else
        {
            ResultsEmptyTitle = "训练进行中";
            ResultsEmptyDescription = "训练完成后将显示最佳模型";
        }
    }

    private void UpdateResultsBestMetric()
    {
        if (!Metrics.TryGetBestPrimaryQuality(out var valueText, out var epochText))
        {
            ResultsBestMap50Text = "—";
            ResultsBestMap50EpochText = string.Empty;
            return;
        }

        ResultsBestMap50Text = valueText;
        ResultsBestMap50EpochText = epochText;
    }

    private void RefreshCheckpointCards()
    {
        var job = _jobs.SelectedJob;
        if (job is null)
        {
            BestCheckpointCard.IsAvailable = false;
            LastCheckpointCard.IsAvailable = false;
            return;
        }

        var bestPath = TrainingJobActionRules.ResolveCheckpointPath(job, TrainingCheckpointSelection.Best);
        var lastPath = TrainingJobActionRules.ResolveCheckpointPath(job, TrainingCheckpointSelection.Last);
        var history = Metrics.GetSelectedJobMetrics();
        var (bestMetric, bestEpoch) = TrainingCheckpointMetricsResolver.ResolveBest(history, job.ModelType);
        var (lastMetric, lastEpoch) = TrainingCheckpointMetricsResolver.ResolveLast(history, job.ModelType);

        BestCheckpointCard.IsAvailable = bestPath is not null;
        BestCheckpointCard.PathText = bestPath ?? "—";
        BestCheckpointCard.MetricText = bestMetric;
        BestCheckpointCard.EpochText = bestEpoch;
        BestCheckpointCard.Footnote = string.Empty;

        LastCheckpointCard.IsAvailable = lastPath is not null;
        LastCheckpointCard.PathText = lastPath ?? "—";
        LastCheckpointCard.MetricText = lastMetric;
        LastCheckpointCard.EpochText = lastEpoch;
        LastCheckpointCard.Footnote = string.Empty;

        if (bestPath is not null
            && lastPath is not null
            && string.Equals(bestPath, lastPath, StringComparison.OrdinalIgnoreCase))
        {
            LastCheckpointCard.Footnote = "仅训练 1 轮，与最佳相同";
        }
        else if (lastPath is null)
        {
            LastCheckpointCard.Footnote = "未生成末轮权重";
        }

        SelectedCheckpointForSave = TrainingJobActionRules.ResolveDefaultSelection(job);
        OnPropertyChanged(nameof(ShowDualCheckpointResults));
        OnPropertyChanged(nameof(ShowSingleCheckpointResults));
        OnPropertyChanged(nameof(CanSaveSelectedCheckpoint));
        OnPropertyChanged(nameof(SaveSelectedCheckpointToolTip));
    }

    private void RefreshMetricsDisplay()
    {
        if (Metrics.RefreshDisplay())
        {
            Preview.RefreshTrainingPlotImages();
        }
    }

    private void EnsureMetricsLoadedFromLog(bool fullReload)
    {
        Metrics.EnsureMetricsLoadedFromLog(fullReload);
        UpdateResultsBestMetric();
    }

    private void StartMetricsPolling(bool fullReload)
    {
        Metrics.StartPolling(fullReload);
        UpdateResultsBestMetric();
    }

    private void NotifySelectedJobMetricContext()
    {
        OnPropertyChanged(nameof(IsSelectedJobClassificationModel));
        Metrics.NotifyJobContextChanged();
        OnPropertyChanged(nameof(ResultsBestMetricLabel));
        Preview.NotifyJobContextChanged();
        OnPropertyChanged(nameof(ShowSingleCheckpointResults));
    }
}
