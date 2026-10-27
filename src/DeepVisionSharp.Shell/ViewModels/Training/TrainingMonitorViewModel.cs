using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.ComponentModel;

using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Training;
using DeepVisionSharp.Shell.Services.Training.Metrics;

namespace DeepVisionSharp.Shell.ViewModels.Training;

/// <summary>
/// Bindable shell for the training page: detail-mode and section state, tab commands, and
/// pass-through of the run monitor's presentation state. Run orchestration lives in
/// <see cref="TrainingRunMonitor"/>.
/// </summary>
public sealed partial class TrainingMonitorViewModel : ObservableObject, IDisposable
{
    private readonly TrainingJobsViewModel _jobs;
    private readonly ITrainingProcessHost _processHost;
    private readonly TrainingRunMonitor _run;

    public TrainingMonitorViewModel(
        TrainingJobsViewModel jobs,
        ITrainingProcessHost processHost,
        IProcessLauncher processLauncher,
        ShellSnackbarNotifier snackbar,
        TrainingLogViewModel log,
        TrainingMetricsPoller metricsPoller,
        TrainingPreviewPoller previewPoller,
        TrainingResourcePoller resourcePoller,
        IClipboardService clipboard,
        IUiDispatcher uiDispatcher)
    {
        _jobs = jobs;
        _processHost = processHost;
        _run = new TrainingRunMonitor(
            jobs,
            log,
            metricsPoller,
            previewPoller,
            resourcePoller,
            processLauncher,
            snackbar,
            clipboard,
            uiDispatcher);
        _run.PropertyChanged += OnRunMonitorPropertyChanged;
    }

    public TrainingMetricsDisplayViewModel Metrics => _run.Metrics;

    public TrainingPreviewPanelViewModel Preview => _run.Preview;

    public TrainingResourcePanelViewModel Resources => _run.Resources;

    public TrainingCheckpointCardViewModel BestCheckpointCard => _run.BestCheckpointCard;

    public TrainingCheckpointCardViewModel LastCheckpointCard => _run.LastCheckpointCard;

    [ObservableProperty]
    private TrainingDetailMode _detailMode = TrainingDetailMode.Settings;

    [ObservableProperty]
    private TrainingSettingsSection _settingsSection = TrainingSettingsSection.Basic;

    [ObservableProperty]
    private TrainingRunSection _runSection = TrainingRunSection.Overview;

    public bool IsRunning => _run.IsRunning;

    public TrainingCheckpointSelection SelectedCheckpointForSave
    {
        get => _run.SelectedCheckpointForSave;
        set => _run.SelectedCheckpointForSave = value;
    }

    public bool IsBestCheckpointSelected
    {
        get => _run.IsBestCheckpointSelected;
        set => _run.IsBestCheckpointSelected = value;
    }

    public bool IsLastCheckpointSelected
    {
        get => _run.IsLastCheckpointSelected;
        set => _run.IsLastCheckpointSelected = value;
    }

    public bool CanSaveSelectedCheckpoint => _run.CanSaveSelectedCheckpoint;

    public string SaveSelectedCheckpointToolTip => _run.SaveSelectedCheckpointToolTip;

    public bool ShowDualCheckpointResults => _run.ShowDualCheckpointResults;

    public bool ShowSingleCheckpointResults => _run.ShowSingleCheckpointResults;

    public bool ShowResultsContent => _run.ShowResultsContent;

    public bool ShowResultsEmptyState => _run.ShowResultsEmptyState;

    public string ResultsEmptyTitle => _run.ResultsEmptyTitle;

    public string ResultsEmptyDescription => _run.ResultsEmptyDescription;

    public string ResultsBestMap50Text => _run.ResultsBestMap50Text;

    public string ResultsBestMap50EpochText => _run.ResultsBestMap50EpochText;

    public string ResultsBestMetricLabel => _run.ResultsBestMetricLabel;

    public bool ShowFailureSummary => _run.ShowFailureSummary;

    public string FailureSummary => _run.FailureSummary;

    public string FailureTraceback => _run.FailureTraceback;

    public bool ShowFailureTraceback => _run.ShowFailureTraceback;

    public bool IsSelectedJobClassificationModel => _run.IsSelectedJobClassificationModel;

    public string TrainingDurationText => _run.TrainingDurationText;

    public string TrainingStartedAtText => _run.TrainingStartedAtText;

    public string TrainingFinishedAtText => _run.TrainingFinishedAtText;

    public bool HasGpuResourceData => _run.Resources.HasGpuResourceData;

    public bool IsSettingsMode => DetailMode == TrainingDetailMode.Settings;

    public bool IsRunMode => DetailMode == TrainingDetailMode.Run;

    public bool IsBasicSectionActive => IsSettingsMode && SettingsSection == TrainingSettingsSection.Basic;

    public bool IsDatasetSectionActive => IsSettingsMode && SettingsSection == TrainingSettingsSection.Dataset;

    public bool IsHyperparametersSectionActive => IsSettingsMode && SettingsSection == TrainingSettingsSection.Hyperparameters;

    public bool IsAdvancedSectionActive => IsSettingsMode && SettingsSection == TrainingSettingsSection.Advanced;

    public bool IsOverviewSectionActive => IsRunMode && RunSection == TrainingRunSection.Overview;

    public bool IsConfigPreviewSectionActive => IsRunMode && RunSection == TrainingRunSection.ConfigPreview;

    public bool IsPreviewSectionActive => IsRunMode && RunSection == TrainingRunSection.Preview;

    public bool IsMetricsSectionActive => IsRunMode && RunSection == TrainingRunSection.Metrics;

    public bool IsLogsSectionActive => IsRunMode && RunSection == TrainingRunSection.Logs;

    public bool IsResourcesSectionActive => IsRunMode && RunSection == TrainingRunSection.Resources;

    public bool AreResultTabsEnabled =>
        _jobs.SelectedJob is not null
        && _jobs.SelectedJob.Status != TrainingJobStatus.Draft;

    public IRelayCommand CopyFailureDetailCommand => _run.CopyFailureDetailCommand;

    public IRelayCommand OpenSelectedCheckpointFolderCommand => _run.OpenSelectedCheckpointFolderCommand;

    public bool IsProcessActive(Guid jobId) => _processHost.IsActive(jobId);

    public void HandleSelectedJobChanged(TrainingJobListItem? job)
    {
        if (job is null)
        {
            _run.NotifyFailurePresentation();
            EnterSettingsMode(TrainingSettingsSection.Basic);
            _run.HandleNoJob();
            return;
        }

        SyncDetailModeForJob(job);
        RefreshTabAvailability();
        _run.NotifyFailurePresentation();
        _run.HandleSelectedJobChanged(job);
    }

    public void HandleJobStatusChanged(Guid jobId, TrainingJobStatus status, string? bestModelPath)
    {
        _run.UpdateRunningStateForStatus(jobId, status);

        if (_jobs.SelectedJob?.Id == jobId)
        {
            _run.NotifyFailurePresentation();
            if (IsSettingsMode && status != TrainingJobStatus.Draft)
            {
                EnterRunMode(TrainingRunSection.Overview);
            }
            else
            {
                _run.RefreshResultsAndMetricsAfterStatus();
            }
        }

        _run.ShowTerminalStatusToast(jobId, status, bestModelPath);
    }

    public void StartLogTracking(Guid jobId, string? logFile, TrainingJobListItem? job)
        => _run.StartLogTracking(jobId, logFile, job);

    public void RefreshRunningState(TrainingJobListItem? job) => _run.RefreshRunningState(job);

    public void AppendLogLine(string line) => _run.AppendLogLine(line);

    public void ClearLogContent() => _run.ClearLogContent();

    public void ResetLogForStart() => _run.ResetLogForStart();

    public void AllowTerminalStatusToasts() => _run.AllowTerminalStatusToasts();

    public void ClearJobMetrics(Guid jobId) => _run.ClearJobMetrics(jobId);

    public void EnterSettingsMode(TrainingSettingsSection section)
    {
        DetailMode = TrainingDetailMode.Settings;
        if (SettingsSection != section)
        {
            SettingsSection = section;
        }
        else
        {
            NotifyDetailState();
        }
    }

    public void EnterRunMode(TrainingRunSection section)
    {
        if (section != TrainingRunSection.Preview)
        {
            Preview.ClosePreviewLightboxCommand.Execute(null);
        }

        DetailMode = TrainingDetailMode.Run;
        if (RunSection != section)
        {
            RunSection = section;
        }
        else
        {
            NotifyDetailState();
            _run.UpdateActiveSection(IsRunMode, section);
            _run.ApplySectionSideEffects();
        }
    }

    public void SyncDetailModeForJob(TrainingJobListItem? job)
    {
        if (job is null || job.Status == TrainingJobStatus.Draft)
        {
            EnterSettingsMode(SettingsSection);
            return;
        }

        EnterRunMode(RunSection);
    }

    public void RefreshTabAvailability()
    {
        OnPropertyChanged(nameof(AreResultTabsEnabled));
        ShowOverviewSectionCommand.NotifyCanExecuteChanged();
        ShowConfigPreviewSectionCommand.NotifyCanExecuteChanged();
        ShowPreviewSectionCommand.NotifyCanExecuteChanged();
        ShowMetricsSectionCommand.NotifyCanExecuteChanged();
        ShowLogsSectionCommand.NotifyCanExecuteChanged();
        SyncDetailModeForJob(_jobs.SelectedJob);
    }

    public void StopAllTabPolling() => _run.StopAllTabPolling();

    public void Dispose()
    {
        _run.PropertyChanged -= OnRunMonitorPropertyChanged;
        _run.Dispose();
    }

    partial void OnDetailModeChanged(TrainingDetailMode value)
    {
        NotifyDetailState();
        _run.UpdateActiveSection(IsRunMode, RunSection);
        _run.ApplySectionSideEffects();
    }

    partial void OnSettingsSectionChanged(TrainingSettingsSection value) => NotifyDetailState();

    partial void OnRunSectionChanged(TrainingRunSection value)
    {
        NotifyDetailState();
        _run.UpdateActiveSection(IsRunMode, value);
        _run.ApplySectionSideEffects();
    }

    private void NotifyDetailState()
    {
        _run.UpdateActiveSection(IsRunMode, RunSection);
        OnPropertyChanged(nameof(IsSettingsMode));
        OnPropertyChanged(nameof(IsRunMode));
        OnPropertyChanged(nameof(IsBasicSectionActive));
        OnPropertyChanged(nameof(IsDatasetSectionActive));
        OnPropertyChanged(nameof(IsHyperparametersSectionActive));
        OnPropertyChanged(nameof(IsAdvancedSectionActive));
        OnPropertyChanged(nameof(IsOverviewSectionActive));
        OnPropertyChanged(nameof(IsConfigPreviewSectionActive));
        OnPropertyChanged(nameof(IsPreviewSectionActive));
        OnPropertyChanged(nameof(IsMetricsSectionActive));
        OnPropertyChanged(nameof(IsLogsSectionActive));
        OnPropertyChanged(nameof(IsResourcesSectionActive));
    }

    private void OnRunMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => OnPropertyChanged(e.PropertyName);

    [RelayCommand]
    private void ShowBasicSection() => EnterSettingsMode(TrainingSettingsSection.Basic);

    [RelayCommand]
    private void ShowDatasetSection() => EnterSettingsMode(TrainingSettingsSection.Dataset);

    [RelayCommand]
    private void ShowHyperparametersSection() => EnterSettingsMode(TrainingSettingsSection.Hyperparameters);

    [RelayCommand]
    private void ShowAdvancedSection() => EnterSettingsMode(TrainingSettingsSection.Advanced);

    [RelayCommand(CanExecute = nameof(AreResultTabsEnabled))]
    private void ShowOverviewSection() => EnterRunMode(TrainingRunSection.Overview);

    [RelayCommand(CanExecute = nameof(AreResultTabsEnabled))]
    private void ShowConfigPreviewSection() => EnterRunMode(TrainingRunSection.ConfigPreview);

    [RelayCommand(CanExecute = nameof(AreResultTabsEnabled))]
    private void ShowPreviewSection() => EnterRunMode(TrainingRunSection.Preview);

    [RelayCommand(CanExecute = nameof(AreResultTabsEnabled))]
    private void ShowMetricsSection() => EnterRunMode(TrainingRunSection.Metrics);

    [RelayCommand(CanExecute = nameof(AreResultTabsEnabled))]
    private void ShowLogsSection() => EnterRunMode(TrainingRunSection.Logs);

    [RelayCommand]
    private void ShowResourcesSection() => EnterRunMode(TrainingRunSection.Resources);
}
