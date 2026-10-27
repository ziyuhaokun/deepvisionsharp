using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;
using System.ComponentModel;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Training;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class TrainingConfigViewModel : ObservableObject,
    ITrainingDraftFormSaverHost,
    ITrainingJobLifecycleHost
{
    private readonly TrainingJobsViewModel _jobs;
    private readonly TrainingMonitorViewModel _monitor;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<TrainingConfigViewModel> _logger;
    private readonly TrainingDraftFormSaver _draftSaver;
    private readonly TrainingJobLifecycleActions _lifecycle;
    private bool _suppressDraftFormSave;

    public TrainingConfigViewModel(
        IMediator mediator,
        TrainingJobsViewModel jobs,
        TrainingMonitorViewModel monitor,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ILogger<TrainingConfigViewModel> logger)
    {
        _jobs = jobs;
        _monitor = monitor;
        _snackbar = snackbar;
        _logger = logger;
        Dataset = new TrainingDatasetViewModel(jobs, snackbar, appDialogService);
        Cls = new TrainingClassificationAugmentationViewModel();
        Spatial = new TrainingSpatialSettingsViewModel();

        // Draft saver must exist before any child PropertyChanged can call ScheduleDraftFormSave.
        // Spatial.SetModelType raises many policy-derived PropertyChanged notifications.
        _draftSaver = new TrainingDraftFormSaver(this, mediator, snackbar, logger);
        _lifecycle = new TrainingJobLifecycleActions(this, mediator, snackbar);

        Dataset.PropertyChanged += OnDatasetPropertyChanged;
        Cls.PropertyChanged += OnChildFormPropertyChanged;
        Spatial.PropertyChanged += OnChildFormPropertyChanged;
        _monitor.PropertyChanged += OnMonitorPropertyChanged;

        _suppressDraftFormSave = true;
        try
        {
            Spatial.SetModelType(SelectedModelType);
        }
        finally
        {
            _suppressDraftFormSave = false;
        }
    }

    public TrainingDatasetViewModel Dataset { get; }

    public TrainingClassificationAugmentationViewModel Cls { get; }

    public TrainingSpatialSettingsViewModel Spatial { get; }

    public ObservableCollection<string> OptimizerOptions { get; } = new(["sgd", "adam", "adamw"]);

    public IReadOnlyList<ModelTypeOption> AvailableModelTypes { get; } =
        ModelTypeNames.AllProductKeys
            .Select(key => new ModelTypeOption(key, ModelTypeNames.GetDisplayLabel(key)))
            .ToList();

    [ObservableProperty]
    private string _modelName = string.Empty;

    [ObservableProperty]
    private string _selectedModelType = ModelTypeNames.Default;

    [ObservableProperty]
    private int _epochs = 50;

    [ObservableProperty]
    private int _batchSize = 8;

    [ObservableProperty]
    private int _earlyStoppingPatience;

    [ObservableProperty]
    private double _learningRate = 0.005;

    [ObservableProperty]
    private string _optimizer = "sgd";

    /// <summary>
    /// True while <see cref="StartTrainingAsync"/> is in flight (local UI feedback before / during Preparing).
    /// </summary>
    [ObservableProperty]
    private bool _isStartingTraining;

    // --- Host interfaces ---

    TrainingJobsViewModel ITrainingDraftFormSaverHost.Jobs => _jobs;
    TrainingJobsViewModel ITrainingJobLifecycleHost.Jobs => _jobs;
    TrainingMonitorViewModel ITrainingJobLifecycleHost.Monitor => _monitor;
    TrainingDatasetViewModel ITrainingJobLifecycleHost.Dataset => Dataset;
    bool ITrainingDraftFormSaverHost.SuppressDraftFormSave => _suppressDraftFormSave;
    bool ITrainingJobLifecycleHost.IsDatasetReadyForSelectedModel => IsDatasetReadyForSelectedModel;
    ModelCapabilities? ITrainingJobLifecycleHost.SelectedModelCapabilities => SelectedModelCapabilities;
    TrainingJobForm ITrainingDraftFormSaverHost.BuildForm() => BuildForm();
    TrainingJobForm ITrainingJobLifecycleHost.BuildForm() => BuildForm();
    string ITrainingJobLifecycleHost.GetDatasetBlockReason() => GetDatasetBlockReason();

    void ITrainingJobLifecycleHost.NotifyCommandStates() => NotifyCommandStates();
    void ITrainingJobLifecycleHost.NotifyDetailState() => NotifyDetailState();

    // --- Derived UI state ---

    /// <summary>Models that use a network input edge length.</summary>
    public bool UsesNetworkInputSize => Spatial.UsesNetworkInputSize;

    public bool IsClassificationModelSelected =>
        ModelTypeNames.IsClassificationModelType(SelectedModelType);

    public string ClsAugmentationSummary =>
        Cls.BuildSummary(IsClassificationModelSelected);

    public bool ShowsClassificationAugmentation => IsClassificationModelSelected;

    public string ClassificationAugmentationSummary => Cls.BuildSummary(IsClassificationModelSelected);

    public bool ShowDatasetSplitControls => true;

    private ModelCapabilities? SelectedModelCapabilities =>
        ModelCapabilityCatalog.TryResolve(SelectedModelType, out var capabilities) ? capabilities : null;

    public string InferredModelTypeDisplay => ModelTypeNames.GetDisplayLabel(SelectedModelType);

    public bool IsFormEditable =>
        _jobs.SelectedJob is not null
        && _jobs.SelectedJob.Status == TrainingJobStatus.Draft;

    public bool IsJobLocked => _jobs.SelectedJob is not null && !IsFormEditable;

    public string SelectedJobStatusDisplay =>
        IsStartingTraining
        && _jobs.SelectedJob?.Status is TrainingJobStatus.Draft
            or TrainingJobStatus.Failed
            or TrainingJobStatus.Cancelled
            ? "启动中"
            : _jobs.SelectedJob is null
                ? "—"
                : TrainingJobUi.GetStatusDisplay(_jobs.SelectedJob.Status);

    public bool CanStartTraining =>
        _jobs.HasProjectContext
        && _jobs.SelectedJob is not null
        && IsDatasetReadyForSelectedModel
        && !_monitor.IsRunning
        && !IsStartingTraining
        && IsFormEditable;

    public bool CanResumeTraining =>
        _jobs.HasProjectContext
        && _jobs.SelectedJob is not null
        && TrainingJobActionRules.CanResume(_jobs.SelectedJob)
        && !_monitor.IsRunning
        && !IsStartingTraining;

    public bool CanStopTraining => _jobs.SelectedJob is not null && _monitor.IsRunning;

    public bool CanDeleteJob =>
        _jobs.SelectedJob is not null
        && !_monitor.IsRunning
        && !_monitor.IsProcessActive(_jobs.SelectedJob.Id);

    public bool CanDuplicateJob =>
        _jobs.SelectedJob is not null
        && TrainingJobTransitions.IsTerminal(_jobs.SelectedJob.Status);

    public bool CanSaveAsModel =>
        _jobs.HasProjectContext
        && _jobs.SelectedJob is not null
        && _monitor.CanSaveSelectedCheckpoint;

    public string StartTrainingToolTip => CanStartTraining ? "开始训练" : GetStartBlockReason();

    public string ResumeTrainingToolTip =>
        CanResumeTraining ? "从 last.pt 继续本次训练" : "当前任务不能续训";

    /// <summary>
    /// Visible footer hint when Start is disabled on a draft job (tooltip alone is easy to miss).
    /// Empty when start is available, or when the job is already running / finished (Run footer).
    /// </summary>
    public string StartTrainingBlockReason => CanStartTraining ? string.Empty : GetStartBlockReason();

    public bool HasStartTrainingBlockReason => !string.IsNullOrWhiteSpace(StartTrainingBlockReason);

    public string StopTrainingToolTip => CanStopTraining ? "停止训练" : "当前没有运行中的训练";

    public string SaveAsModelToolTip => _monitor.SaveSelectedCheckpointToolTip;

    public string DeleteJobToolTip =>
        CanDeleteJob
            ? "删除训练任务"
            : "训练准备中或运行中无法删除，请先停止";

    private bool IsDatasetReadyForSelectedModel =>
        SelectedModelCapabilities?.Family == ModelFamily.Classification
        && Dataset.IsDatasetConfigValid;

    public void HandleSelectedJobChanged(TrainingJobListItem? job, CancellationToken cancellationToken = default)
    {
        CancelPendingDraftFormSave();

        if (job is null)
        {
            NotifyDetailState();
            return;
        }

        try
        {
            // Apply form first, fully, on the UI thread — do NOT start library refresh in parallel.
            // Concurrent ObservableCollection mutations during NumberBox/layout updates hard-crashed
            // the process at "selecting first job" with no managed exception log.
            ApplyFormFromJob(job.Source);
            NotifyCommandStates();
            NotifyDetailState();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply training form for job {JobId}", job.Id);
            _snackbar.ShowError($"加载任务配置失败：{ex.Message}", "训练");
            try
            {
                NotifyCommandStates();
                NotifyDetailState();
            }
            catch
            {
                // ignore secondary UI refresh failures
            }

            return;
        }
    }

    public void HandleProjectWorkflowChanged()
    {
        Spatial.NotifyProjectWorkflowChanged();
        Dataset.NotifyProjectWorkflowChanged();
        OnPropertyChanged(nameof(ShowsClassificationAugmentation));
        OnPropertyChanged(nameof(ClassificationAugmentationSummary));
        OnPropertyChanged(nameof(ShowDatasetSplitControls));
        NotifyCommandStates();
    }

    /// <summary>
    /// Optional numeric text sync. Prefer TwoWay Value bindings; kept for call sites that
    /// still push parsed text. Honors draft-apply suppress to avoid re-entrancy storms.
    /// </summary>
    public void SyncNumericConfig(TrainingNumericField field, string? text)
    {
        if (_suppressDraftFormSave || !IsFormEditable || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        switch (field)
        {
            case TrainingNumericField.Epochs when int.TryParse(text, out var epochs):
                Epochs = epochs;
                break;
            case TrainingNumericField.BatchSize when int.TryParse(text, out var batchSize):
                BatchSize = batchSize;
                break;
            case TrainingNumericField.LearningRate when double.TryParse(text, out var learningRate):
                LearningRate = learningRate;
                break;
            case TrainingNumericField.ValidationRatio when double.TryParse(text, out var validationRatio):
                Dataset.ValidationRatio = validationRatio;
                break;
            case TrainingNumericField.NetworkInputSize when int.TryParse(text, out var squareSize):
                Spatial.SetNetworkInputSizeFromUser(squareSize);
                break;
            case TrainingNumericField.NetworkInputWidth when int.TryParse(text, out var inputWidth):
                Spatial.SetNetworkInputWidthFromUser(inputWidth);
                break;
            case TrainingNumericField.NetworkInputHeight when int.TryParse(text, out var inputHeight):
                Spatial.SetNetworkInputHeightFromUser(inputHeight);
                break;
        }

        ScheduleDraftFormSave();
    }

    partial void OnModelNameChanged(string value)
    {
        SyncSelectedJobName(value);
        ScheduleDraftFormSave();
    }

    partial void OnSelectedModelTypeChanged(string value)
    {
        OnPropertyChanged(nameof(IsClassificationModelSelected));
        OnPropertyChanged(nameof(ClsAugmentationSummary));
        OnPropertyChanged(nameof(ShowsClassificationAugmentation));
        OnPropertyChanged(nameof(ClassificationAugmentationSummary));
        OnPropertyChanged(nameof(ShowDatasetSplitControls));
        OnPropertyChanged(nameof(InferredModelTypeDisplay));
        // Re-normalize size when switching family so the value stays inside the new range.
        Spatial.SetModelType(value);
        OnPropertyChanged(nameof(UsesNetworkInputSize));

        ScheduleDraftFormSave();
    }

    partial void OnEpochsChanged(int value) => ScheduleDraftFormSave();

    partial void OnBatchSizeChanged(int value) => ScheduleDraftFormSave();

    partial void OnLearningRateChanged(double value) => ScheduleDraftFormSave();

    partial void OnOptimizerChanged(string value) => ScheduleDraftFormSave();

    [RelayCommand(CanExecute = nameof(CanSaveAsModel))]
    private Task SaveAsModelAsync() => _lifecycle.SaveAsModelAsync();

    [RelayCommand(CanExecute = nameof(CanStartTraining))]
    private Task StartTrainingAsync() => _lifecycle.StartTrainingAsync(resume: false);

    [RelayCommand(CanExecute = nameof(CanResumeTraining))]
    private Task ResumeTrainingAsync() => _lifecycle.StartTrainingAsync(resume: true);

    partial void OnIsStartingTrainingChanged(bool value)
    {
        NotifyCommandStates();
        OnPropertyChanged(nameof(SelectedJobStatusDisplay));
    }

    [RelayCommand(CanExecute = nameof(CanStopTraining))]
    private Task StopTrainingAsync() => _lifecycle.StopTrainingAsync();

    public void NotifyCommandStates()
    {
        StartTrainingCommand.NotifyCanExecuteChanged();
        ResumeTrainingCommand.NotifyCanExecuteChanged();
        StopTrainingCommand.NotifyCanExecuteChanged();
        SaveAsModelCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanStartTraining));
        OnPropertyChanged(nameof(CanResumeTraining));
        OnPropertyChanged(nameof(ResumeTrainingToolTip));
        OnPropertyChanged(nameof(CanStopTraining));
        OnPropertyChanged(nameof(CanDeleteJob));
        OnPropertyChanged(nameof(CanDuplicateJob));
        OnPropertyChanged(nameof(CanSaveAsModel));
        OnPropertyChanged(nameof(StartTrainingToolTip));
        OnPropertyChanged(nameof(StartTrainingBlockReason));
        OnPropertyChanged(nameof(HasStartTrainingBlockReason));
        OnPropertyChanged(nameof(StopTrainingToolTip));
        OnPropertyChanged(nameof(SaveAsModelToolTip));
        OnPropertyChanged(nameof(DeleteJobToolTip));
    }

    public void NotifyDetailState()
    {
        OnPropertyChanged(nameof(IsFormEditable));
        OnPropertyChanged(nameof(IsJobLocked));
        OnPropertyChanged(nameof(SelectedJobStatusDisplay));
    }

    public void CancelPendingDraftFormSave() => _draftSaver.CancelPending();

    private TrainingJobForm BuildForm()
    {
        var form = new TrainingJobForm
        {
            ModelName = ModelName.Trim(),
            ModelType = SelectedModelType,
            Epochs = Epochs,
            BatchSize = BatchSize,
            EarlyStoppingPatience = EarlyStoppingPatience,
            Lr = LearningRate,
            Optimizer = Optimizer,
            ClassificationAugmentation = IsClassificationModelSelected
                ? Cls.Build()
                : null
        };
        Dataset.ApplyToForm(form);
        Spatial.ApplyToForm(form);
        return form;
    }

    private void SyncSelectedJobName(string value)
    {
        if (_suppressDraftFormSave || _jobs.SelectedJob is null)
        {
            return;
        }

        if (!string.Equals(_jobs.SelectedJob.Name, value, StringComparison.Ordinal))
        {
            _jobs.SelectedJob.Name = value;
            _jobs.SelectedJob.Source.Name = value;
        }
    }

    private void ScheduleDraftFormSave() => _draftSaver.ScheduleSave();

    private void ApplyFormFromJob(TrainingJobRow row)
    {
        _logger.LogInformation("ApplyFormFromJob begin for {JobName} ({ModelType})", row.Name, row.ModelType);

        _suppressDraftFormSave = true;
        Spatial.SuppressUserEditTracking = true;
        try
        {
            // Minimal path for empty drafts (no persisted configuration): avoid multi-pass
            // Reset+reapply property storms that re-enter WPF-UI NumberBox layout.
            var hasPersistedConfig = row.ConfigEpochs is not null
                || row.ConfigBatchSize is not null
                || row.ConfigLearningRate is not null
                || row.ConfigClassificationAugmentation is not null
                || row.ConfigNetworkInputWidth is not null
                || row.ConfigNetworkInputHeight is not null
                || row.ConfigNetworkInputSize is not null
                || (row.ConfigGalleryIds is { Count: > 0 })
                || row.ConfigGalleryId is not null
                || (row.ConfigGalleryRoles is { Count: > 0 });

            if (!hasPersistedConfig)
            {
                ModelName = row.Name;
                SelectedModelType = string.IsNullOrWhiteSpace(row.ModelType)
                    ? string.Empty
                    : row.ModelType.Trim();
                Epochs = 50;
                BatchSize = 8;
                EarlyStoppingPatience = 0;
                LearningRate = 0.005;
                Optimizer = "sgd";
                Spatial.SetModelType(SelectedModelType);
                Spatial.ResetToDefaults();
                Spatial.TryApplySuggestedDefault(isFormEditable: true, suppressDraftSave: true);
                Cls.ResetToDefaults();
                Dataset.ResetForNewJob();
            }
            else
            {
                ResetFormToDefaults();
                ModelName = row.Name;
                SelectedModelType = string.IsNullOrWhiteSpace(row.ModelType)
                    ? string.Empty
                    : row.ModelType.Trim();
                Dataset.ApplyFromJob(row);

                if (row.ConfigEpochs is int epochs)
                {
                    Epochs = epochs;
                }

                if (row.ConfigBatchSize is int batchSize)
                {
                    BatchSize = batchSize;
                }

                if (row.ConfigEarlyStoppingPatience is int patience)
                {
                    EarlyStoppingPatience = patience;
                }

                if (row.ConfigLearningRate is double learningRate)
                {
                    LearningRate = learningRate;
                }

                if (!string.IsNullOrWhiteSpace(row.ConfigOptimizer))
                {
                    Optimizer = row.ConfigOptimizer;
                }

                var width = row.ConfigNetworkInputWidth
                    ?? row.ConfigNetworkInputSize
                    ?? Spatial.DefaultNetworkInputSize();
                var height = row.ConfigNetworkInputHeight
                    ?? row.ConfigNetworkInputSize
                    ?? width;
                Spatial.SetNetworkInputWidthFromUser(width);
                Spatial.SetNetworkInputHeightFromUser(height);

                Cls.Apply(row.ConfigClassificationAugmentation);
                Spatial.TryApplySuggestedDefault(isFormEditable: true, suppressDraftSave: true);
            }
        }
        finally
        {
            Spatial.SuppressUserEditTracking = false;
            _suppressDraftFormSave = false;
        }

        NotifyDetailState();
        _logger.LogInformation("ApplyFormFromJob end for {JobName}", row.Name);
    }

    private void ResetFormToDefaults()
    {
        ModelName = TrainingJobsViewModel.CreateDefaultJobName();
        SelectedModelType = ModelTypeNames.Default;
        Epochs = 50;
        BatchSize = 8;
        EarlyStoppingPatience = 0;
        LearningRate = 0.005;
        Optimizer = "sgd";
        Spatial.SetModelType(SelectedModelType);
        Spatial.ResetToDefaults();
        Cls.ResetToDefaults();
        Dataset.ResetForNewJob();
    }

    private string GetStartBlockReason() => _lifecycle.GetStartBlockReason();

    private string GetDatasetBlockReason() => _lifecycle.GetDatasetBlockReason();

    private void OnMonitorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrainingMonitorViewModel.CanSaveSelectedCheckpoint)
            or nameof(TrainingMonitorViewModel.SelectedCheckpointForSave)
            or nameof(TrainingMonitorViewModel.SaveSelectedCheckpointToolTip)
            or nameof(TrainingMonitorViewModel.ShowResultsContent))
        {
            SaveAsModelCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanSaveAsModel));
            OnPropertyChanged(nameof(SaveAsModelToolTip));
        }
    }

    private void OnChildFormPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDraftFormSave)
        {
            return;
        }

        if (sender is TrainingClassificationAugmentationViewModel)
        {
            OnPropertyChanged(nameof(ClsAugmentationSummary));
        }

        if (sender is TrainingClassificationAugmentationViewModel)
        {
            OnPropertyChanged(nameof(ClassificationAugmentationSummary));
        }

        ScheduleDraftFormSave();
    }

    private void OnDatasetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDraftFormSave)
        {
            return;
        }

        if (e.PropertyName is nameof(TrainingDatasetViewModel.IsDatasetConfigValid)
            or nameof(TrainingDatasetViewModel.DatasetValidationMessage)
            or nameof(TrainingDatasetViewModel.ValidationRatio)
            or nameof(TrainingDatasetViewModel.DatasetSplitMode)
            or nameof(TrainingDatasetViewModel.EstimatedTrainSamples)
            or nameof(TrainingDatasetViewModel.EstimatedValSamples)
            or nameof(TrainingDatasetViewModel.IsPurposeAssignmentMode)
            or nameof(TrainingDatasetViewModel.DatasetSummaryText))
        {
            NotifyCommandStates();
            Spatial.TryApplySuggestedDefault(IsFormEditable, _suppressDraftFormSave);
        }

        ScheduleDraftFormSave();
    }
}
