using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class TrainingDatasetViewModel : ObservableObject
{
    private readonly TrainingJobsViewModel _jobs;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private bool _suppressChanges;

    public TrainingDatasetViewModel(
        TrainingJobsViewModel jobs,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService)
    {
        _jobs = jobs;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
    }

    [ObservableProperty]
    private TrainingDatasetSplitMode _datasetSplitMode = TrainingDatasetSplitMode.AutoSplit;

    [ObservableProperty]
    private double _validationRatio = 0.1;

    [ObservableProperty]
    private int _estimatedTrainSamples;

    [ObservableProperty]
    private int _estimatedValSamples;

    [ObservableProperty]
    private string _datasetSummaryText = string.Empty;

    [ObservableProperty]
    private string? _datasetValidationMessage;

    public bool IsAutoSplitMode => DatasetSplitMode == TrainingDatasetSplitMode.AutoSplit;

    public bool IsPurposeAssignmentMode => DatasetSplitMode == TrainingDatasetSplitMode.PurposeAssignment;

    public bool IsRatioVisible => IsAutoSplitMode;

    public bool ShowsStandardGalleryRoles => IsPurposeAssignmentMode;

    public bool ShowsInteractiveGalleryPicker => true;

    public bool IsDatasetConfigValid =>
        EstimatedTrainSamples > 0
        && EstimatedValSamples > 0
        && string.IsNullOrWhiteSpace(DatasetValidationMessage);

    public bool HasDatasetValidationMessage => !string.IsNullOrWhiteSpace(DatasetValidationMessage);

    public double ValidationRatioPercent
    {
        get => ValidationRatio * 100;
        set => ValidationRatio = Math.Clamp(value / 100.0, 0, 0.5);
    }

    /// <summary>自动分割对照条 / 滑条分界：训练集占比（%）。写入时换算为验证比例并夹到 0–50。</summary>
    public double TrainRatioPercent
    {
        get => 100.0 - ValidationRatioPercent;
        set => ValidationRatioPercent = 100.0 - value;
    }

    public void NotifyProjectWorkflowChanged()
    {
        OnPropertyChanged(nameof(IsAutoSplitMode));
        OnPropertyChanged(nameof(IsPurposeAssignmentMode));
        OnPropertyChanged(nameof(IsRatioVisible));
        OnPropertyChanged(nameof(ShowsStandardGalleryRoles));
        OnPropertyChanged(nameof(ShowsInteractiveGalleryPicker));
        RefreshEstimates();
    }

    public void ResetForNewJob()
    {
        _suppressChanges = true;
        try
        {
            DatasetSplitMode = TrainingDatasetSplitMode.AutoSplit;
            ValidationRatio = 0.1;
            ClearGalleryAssignments();
        }
        finally
        {
            _suppressChanges = false;
        }

        RefreshEstimates();
    }

    public void AttachGalleryChangeHandlers()
    {
        foreach (var gallery in _jobs.Galleries)
        {
            gallery.PropertyChanged -= OnGalleryPropertyChanged;
            gallery.PropertyChanged += OnGalleryPropertyChanged;
        }

        RefreshEstimates();
    }

    public void ApplyFromJob(TrainingJobRow row)
    {
        _suppressChanges = true;
        try
        {
            DatasetSplitMode = row.ConfigDatasetSplitMode ?? TrainingDatasetSplitMode.AutoSplit;
            ValidationRatio = row.ConfigValidationRatio ?? 0.1;

            foreach (var gallery in _jobs.Galleries)
            {
                gallery.PropertyChanged -= OnGalleryPropertyChanged;
            }

            {
                var roles = row.ConfigGalleryRoles ?? new Dictionary<Guid, GalleryDatasetRole>();
                var selectedIds = row.ConfigGalleryIds?.Where(id => id != Guid.Empty).Distinct().ToArray()
                    ?? Array.Empty<Guid>();
                if (selectedIds.Length == 0 && row.ConfigGalleryId is Guid legacyId)
                {
                    selectedIds = [legacyId];
                }

                foreach (var gallery in _jobs.Galleries)
                {
                    if (DatasetSplitMode == TrainingDatasetSplitMode.PurposeAssignment)
                    {
                        gallery.IsSelected = false;
                        gallery.Role = roles.TryGetValue(gallery.Id, out var role)
                            ? role
                            : GalleryDatasetRole.None;
                    }
                    else
                    {
                        gallery.Role = GalleryDatasetRole.None;
                        gallery.IsSelected = selectedIds.Contains(gallery.Id);
                    }

                    gallery.PropertyChanged += OnGalleryPropertyChanged;
                }
            }
        }
        finally
        {
            _suppressChanges = false;
        }

        RefreshEstimates();
    }

    public void ApplyToForm(TrainingJobForm form)
    {
        form.DatasetSplitMode = DatasetSplitMode;
        form.ValidationRatio = ValidationRatio;
        form.SelectedGalleryIds = _jobs.Galleries
            .Where(gallery => gallery.IsSelected)
            .Select(gallery => gallery.Id)
            .ToList();
        form.GalleryRoles = _jobs.Galleries
            .Where(gallery => gallery.Role != GalleryDatasetRole.None)
            .ToDictionary(gallery => gallery.Id, gallery => gallery.Role);
    }

    public void RefreshEstimates()
    {
        var sampleCounts = _jobs.Galleries.ToDictionary(
            g => g.Id,
            g => g.AnnotatedCount);
        var form = new TrainingJobForm();
        ApplyToForm(form);

        var estimate = TrainingDatasetConfigRules.Estimate(
            DatasetSplitMode,
            form.GalleryRoles,
            TrainingJobFormMapper.ResolveParticipatingGalleryIds(form),
            sampleCounts,
            ValidationRatio);

        EstimatedTrainSamples = estimate.TrainSamples;
        EstimatedValSamples = estimate.ValSamples;
        DatasetValidationMessage = estimate.ErrorMessage;

        DatasetSummaryText = BuildSummaryText(form);
        OnPropertyChanged(nameof(ValidationRatioPercent));
        OnPropertyChanged(nameof(TrainRatioPercent));
        OnPropertyChanged(nameof(IsDatasetConfigValid));
        OnPropertyChanged(nameof(HasDatasetValidationMessage));
        OnPropertyChanged(nameof(IsAutoSplitMode));
        OnPropertyChanged(nameof(IsPurposeAssignmentMode));
        OnPropertyChanged(nameof(IsRatioVisible));
        OnPropertyChanged(nameof(ShowsStandardGalleryRoles));
        OnPropertyChanged(nameof(ShowsInteractiveGalleryPicker));
    }

    [RelayCommand]
    private async Task SwitchToAutoSplitModeAsync()
    {
        if (DatasetSplitMode == TrainingDatasetSplitMode.AutoSplit)
        {
            return;
        }

        if (!await ConfirmModeSwitchAsync())
        {
            return;
        }

        DatasetSplitMode = TrainingDatasetSplitMode.AutoSplit;
        ClearGalleryRoles();
        RefreshEstimates();
    }

    [RelayCommand]
    private async Task SwitchToPurposeAssignmentModeAsync()
    {
        if (DatasetSplitMode == TrainingDatasetSplitMode.PurposeAssignment)
        {
            return;
        }

        if (!await ConfirmModeSwitchAsync())
        {
            return;
        }

        DatasetSplitMode = TrainingDatasetSplitMode.PurposeAssignment;
        ClearGallerySelections();
        RefreshEstimates();
    }

    [RelayCommand]
    private void SetGalleryTraining(GalleryOption? gallery) => AssignGalleryRole(gallery, GalleryDatasetRole.Training);

    [RelayCommand]
    private void SetGalleryValidation(GalleryOption? gallery) => AssignGalleryRole(gallery, GalleryDatasetRole.Validation);

    [RelayCommand]
    private void SetGalleryTrainingAndValidation(GalleryOption? gallery) =>
        AssignGalleryRole(gallery, GalleryDatasetRole.TrainingAndValidation);

    [RelayCommand]
    private void SetGalleryNone(GalleryOption? gallery) => AssignGalleryRole(gallery, GalleryDatasetRole.None);

    private void AssignGalleryRole(GalleryOption? gallery, GalleryDatasetRole role)
    {
        if (_suppressChanges || gallery is null)
        {
            return;
        }

        gallery.Role = role;
        RefreshEstimates();
    }

    partial void OnDatasetSplitModeChanged(TrainingDatasetSplitMode value)
    {
        if (_suppressChanges)
        {
            return;
        }

        OnPropertyChanged(nameof(IsAutoSplitMode));
        OnPropertyChanged(nameof(IsPurposeAssignmentMode));
        OnPropertyChanged(nameof(IsRatioVisible));
        OnPropertyChanged(nameof(ShowsStandardGalleryRoles));
        RefreshEstimates();
    }

    partial void OnValidationRatioChanged(double value)
    {
        if (_suppressChanges)
        {
            return;
        }

        OnPropertyChanged(nameof(ValidationRatioPercent));
        OnPropertyChanged(nameof(TrainRatioPercent));
        RefreshEstimates();
    }

    private void OnGalleryPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_suppressChanges)
        {
            return;
        }

        if (e.PropertyName is nameof(GalleryOption.IsSelected) or nameof(GalleryOption.Role))
        {
            RefreshEstimates();
        }
    }

    private async Task<bool> ConfirmModeSwitchAsync()
    {
        var hasConfig = _jobs.Galleries.Any(g => g.IsSelected || g.Role != GalleryDatasetRole.None);
        if (!hasConfig)
        {
            return true;
        }

        var result = await _appDialogService.ShowAsync(
            AppDialogKind.Confirm,
            new ConfirmDialogRequest("切换划分模式将清空当前数据集配置，是否继续？"),
            new DialogOptions(
                Title: "切换数据集模式",
                PrimaryButtonText: "继续",
                DefaultButton: AppDialogDefaultButton.Close));

        return result == AppDialogResult.Primary;
    }

    private void ClearGalleryAssignments()
    {
        foreach (var gallery in _jobs.Galleries)
        {
            gallery.PropertyChanged -= OnGalleryPropertyChanged;
            gallery.IsSelected = false;
            gallery.Role = GalleryDatasetRole.None;
            gallery.PropertyChanged += OnGalleryPropertyChanged;
        }
    }

    private void ClearGalleryRoles()
    {
        foreach (var gallery in _jobs.Galleries)
        {
            gallery.Role = GalleryDatasetRole.None;
        }
    }

    private void ClearGallerySelections()
    {
        foreach (var gallery in _jobs.Galleries)
        {
            gallery.IsSelected = false;
        }
    }

    private string BuildSummaryText(TrainingJobForm form)
    {
        if (DatasetSplitMode == TrainingDatasetSplitMode.PurposeAssignment)
        {
            var train = _jobs.Galleries.Count(g => g.Role == GalleryDatasetRole.Training);
            var val = _jobs.Galleries.Count(g => g.Role == GalleryDatasetRole.Validation);
            var dual = _jobs.Galleries.Count(g => g.Role == GalleryDatasetRole.TrainingAndValidation);
            return $"训练 {train} · 验证 {val} · 训&验 {dual} · 预估 {EstimatedTrainSamples}/{EstimatedValSamples}";
        }

        var selected = form.SelectedGalleryIds.Count;
        return $"已选 {selected} 图库 · 预估训练 {EstimatedTrainSamples} / 验证 {EstimatedValSamples} · 比例 {ValidationRatio:P0}";
    }
}
