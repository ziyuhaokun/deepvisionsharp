using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Dialogs;
using DeepVisionSharp.Presentation.Services.Labeling;
using DeepVisionSharp.Presentation.Services.Labeling.InferenceDialogs;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services.Labeling.WriteBack;

namespace DeepVisionSharp.Presentation.ViewModels.Labeling;

/// <summary>
/// Labeling-page auto-inference façade: toolbar commands, shared runtime state, and
/// routing to a per-workflow dialog session (exclusive XAML + business rules).
/// </summary>
public sealed partial class LabelingInferenceViewModel : ObservableObject, ILabelingInferenceHost
{
    private readonly LabelingWorkspaceContext _context;
    private readonly LabelingEditorViewModel _editor;
    private readonly LabelingLabelsViewModel _labels;
    private readonly IAppDialogService _appDialogService;
    private readonly ITaskCapabilityResolver _taskCapabilities;
    private readonly LabelingInferenceWriteBackRegistry _writeBackRegistry;
    private readonly LabelingInferenceModelCatalog _catalog;
    private readonly LabelingInferenceRunner _runner;
    private readonly LabelingInferenceDebug _debug;
    private InferenceSettingsSnapshot? _inferenceSettingsBackup;
    private bool? _lastIsClassificationProject;

    public LabelingInferenceViewModel(
        LabelingWorkspaceContext context,
        IMediator mediator,
        LabelingEditorViewModel editor,
        LabelingLabelsViewModel labels,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ITaskCapabilityResolver taskCapabilities,
        LabelingInferenceWriteBackRegistry writeBackRegistry,
        ShellTaskCenterViewModel taskCenter,
        IClipboardService clipboard,
        ILogger<LabelingInferenceViewModel> logger)
    {
        _context = context;
        _editor = editor;
        _labels = labels;
        _appDialogService = appDialogService;
        _taskCapabilities = taskCapabilities;
        _writeBackRegistry = writeBackRegistry;
        _catalog = new LabelingInferenceModelCatalog(this, mediator, taskCapabilities);
        _runner = new LabelingInferenceRunner(this, mediator, snackbar, taskCenter, logger);
        _debug = new LabelingInferenceDebug(this, mediator, appDialogService, snackbar, clipboard, logger, _catalog);

        _context.PropertyChanged += OnContextPropertyChanged;
        ApplyWorkflowDefaultThresholdsIfChanged();
        RefreshInferenceDevices();
    }

    public void CancelPendingInference() => _runner.Cancel();

    private IInferenceDialogSession ActiveSession =>
        InferenceDialogSessionFactory.Resolve(_context.WorkflowKind)
        ?? ClassificationInferenceDialogSession.Instance;

    LabelingWorkspaceContext ILabelingInferenceHost.Context => _context;

    LabelingEditorViewModel ILabelingInferenceHost.Editor => _editor;

    LabelingLabelsViewModel ILabelingInferenceHost.Labels => _labels;

    object ILabelingInferenceHost.DialogDataContext => this;

    LabelingInferenceWriteBackRegistry ILabelingInferenceHost.WriteBackRegistry => _writeBackRegistry;

    bool ILabelingInferenceHost.IsClassificationProject => _context.IsClassificationProject;

    InferenceConfig ILabelingInferenceHost.BuildInferenceConfig() => BuildInferenceConfig();

    Result<ResolvedTaskProfile> ILabelingInferenceHost.TryResolveSelectedProfile() =>
        TryResolveSelectedProfile();

    bool ILabelingInferenceHost.TryValidateInferenceDraft() => TryValidateInferenceDraft();

    void ILabelingInferenceHost.NotifyModelsListChanged()
    {
        OnPropertyChanged(nameof(HasInferenceModels));
        OnPropertyChanged(nameof(HasNoInferenceModels));
    }

    void ILabelingInferenceHost.NotifyInferenceConfigState() => NotifyInferenceConfigState();

    void ILabelingInferenceHost.NotifyInferenceCommandsCanExecute() => NotifyInferenceCommandsCanExecute();

    public ObservableCollection<InferenceModelOption> InferenceModels { get; } = new();

    [ObservableProperty]
    private InferenceModelOption? _selectedInferenceModel;

    [ObservableProperty]
    private double _inferenceConfidence = 0.5;

    [ObservableProperty]
    private double _inferenceNmsIou = 0.45;

    [ObservableProperty]
    private int _inferenceMaxDetections = 100;

    [ObservableProperty]
    private int _inferenceBatchSize = 8;

    [ObservableProperty]
    private InferenceDeviceOption? _selectedInferenceDevice;

    [ObservableProperty]
    private string _inferenceDialogErrorMessage = string.Empty;

    public ObservableCollection<InferenceDeviceOption> InferenceDeviceOptions { get; } = new();

    [ObservableProperty]
    private string _inferenceDebugHeadline = string.Empty;

    [ObservableProperty]
    private string _inferenceDebugReport = string.Empty;

    public bool HasInferenceDialogErrorMessage => !string.IsNullOrWhiteSpace(InferenceDialogErrorMessage);

    public bool HasInferenceModels => InferenceModels.Count > 0;

    public bool HasNoInferenceModels => InferenceModels.Count == 0;

    public bool IsInferenceConfigValid =>
        BuildInferenceConfig().IsValid && IsSelectedModelSpatialPolicyCompatible;

    private bool IsSelectedModelSpatialPolicyCompatible
    {
        get
        {
            if (SelectedInferenceModel is null)
            {
                return false;
            }

            var profileResult = TryResolveSelectedProfile();
            return profileResult.IsSuccess
                && profileResult.Value is not null
                && LabelingInferenceModelCatalog.IsSpatialPolicyCompatible(
                    profileResult.Value,
                    SelectedInferenceModel);
        }
    }

    public bool ShowInferenceConfigWarning => HasInferenceModels && !IsInferenceConfigValid;

    public string InferenceDeviceSummary => SelectedInferenceDevice?.DisplayName ?? "未选择设备";

    public string InferenceConfigSummary => ActiveSession.FormatConfigSummary(CreateBuildContext());

    public string InferenceSpatialPolicySummary =>
        SelectedInferenceModel?.SpatialPolicySummary ?? "请选择模型以查看策略";

    public string InferencePolicySectionTitle => ActiveSession.PolicySectionTitle;

    public string InferenceButtonToolTip => ActiveSession.FormatButtonToolTip(CreateBuildContext());

    /// <summary>自动推理按钮上的动作文案（deepvision 页面绑定它）：按项目能力取词，本仓库只有自动标注一条路。</summary>
    public string InferenceActionLabel =>
        _context.ProjectProfile.SupportsImageClass ? "分类自动标注" : "YOLO 自动标注";

    public string InferenceDialogDescription => ActiveSession.Description;

    public string InferenceNoModelsHint => ActiveSession.NoModelsHint;

    public string InferenceParametersFooterHint => ActiveSession.FooterHint;

    public string ConfidenceThresholdHint => ActiveSession.ConfidenceHint;

    public string? InferenceFixedBatchHint => ActiveSession.FixedBatchHint(SelectedInferenceModel);

    public bool HasInferenceFixedBatchHint => !string.IsNullOrWhiteSpace(InferenceFixedBatchHint);

    public bool IsClassificationModelSelected =>
        SelectedInferenceModel is not null
        && ModelTypeNames.IsClassificationModelType(SelectedInferenceModel.ModelType);

    /// <summary>Detection-style max-det control; driven by active workflow session.</summary>
    public bool ShowMaxDetectionsParameter => ActiveSession.ShowsMaxDetections(SelectedInferenceModel);

    public bool ShowBatchSizeParameter => ActiveSession.ShowsBatchSize(SelectedInferenceModel);

    public bool CanOpenInferenceDialog =>
        IsVisible
        && _context.IsNotBusy
        && _context.HasImage
        && _context.Gallery is not null
        && InferenceDialogSessionFactory.Resolve(_context.WorkflowKind) is not null;

    public bool IsVisible => InferenceDialogSessionFactory.Resolve(_context.WorkflowKind) is not null;

    [RelayCommand(CanExecute = nameof(CanOpenInferenceDialog))]
    private async Task ShowAutoInferenceDialogAsync()
    {
        var session = InferenceDialogSessionFactory.Resolve(_context.WorkflowKind);
        if (session is null)
        {
            return;
        }

        await LoadInferenceModelsAsync();
        PrepareInferenceDraft();

        while (true)
        {
            var result = await _appDialogService.ShowAsync(session.DialogKind, this);
            if (result != AppDialogResult.Primary)
            {
                CancelInferenceDraft();
                return;
            }

            if (TryValidateInferenceDraft())
            {
                break;
            }
        }

        await _runner.RunAsync();
    }

    [RelayCommand(CanExecute = nameof(CanOpenInferenceDialog))]
    private Task RunAutoInferenceAsync() => _runner.RunAsync();

    /// <summary>
    /// Dry-run classification inference and show a diagnostic report (does not mutate annotations).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanOpenInferenceDialog))]
    private Task RunInferenceDebugAsync() => _debug.RunAsync();

    public Task LoadInferenceModelsAsync(CancellationToken cancellationToken = default) =>
        _catalog.LoadAsync(cancellationToken);

    public void PrepareInferenceDraft()
    {
        RefreshInferenceDevices();
        _inferenceSettingsBackup = new InferenceSettingsSnapshot(
            SelectedInferenceModel,
            InferenceConfidence,
            InferenceNmsIou,
            InferenceMaxDetections,
            InferenceBatchSize,
            SelectedInferenceDevice);
        InferenceDialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasInferenceDialogErrorMessage));
        NotifyInferenceConfigState();
    }

    public void CancelInferenceDraft()
    {
        if (_inferenceSettingsBackup is null)
        {
            return;
        }

        SelectedInferenceModel = _inferenceSettingsBackup.SelectedModel;
        InferenceConfidence = _inferenceSettingsBackup.Confidence;
        InferenceNmsIou = _inferenceSettingsBackup.NmsIou;
        InferenceMaxDetections = _inferenceSettingsBackup.MaxDetections;
        InferenceBatchSize = _inferenceSettingsBackup.BatchSize;
        SelectedInferenceDevice = _inferenceSettingsBackup.Device
            ?? InferenceDeviceOptions.FirstOrDefault(item => item.Device == InferenceDevices.PrimaryGpu)
            ?? InferenceDeviceOptions.FirstOrDefault();
        _inferenceSettingsBackup = null;
        InferenceDialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasInferenceDialogErrorMessage));
        NotifyInferenceConfigState();
    }

    public bool TryValidateInferenceDraft()
    {
        if (!HasInferenceModels)
        {
            SetInferenceDialogError(InferenceNoModelsHint);
            return false;
        }

        if (SelectedInferenceModel is null)
        {
            SetInferenceDialogError("请选择推理模型。");
            return false;
        }

        var profileResult = TryResolveSelectedProfile();
        if (profileResult.IsFailure || profileResult.Value is null)
        {
            SetInferenceDialogError(profileResult.Error ?? "项目与模型不兼容，无法推理。");
            return false;
        }

        if (profileResult.Value.WriteBackKind is InferenceWriteBackKind.None)
        {
            SetInferenceDialogError("所选模型没有可用的自动推理写回策略。");
            return false;
        }

        if (!_writeBackRegistry.TryResolve(profileResult.Value.WriteBackKind, out _))
        {
            SetInferenceDialogError($"未注册写回策略：{profileResult.Value.WriteBackKind}");
            return false;
        }

        if (!File.Exists(SelectedInferenceModel.CheckpointPath))
        {
            SetInferenceDialogError("所选模型文件不存在。");
            return false;
        }

        if (!LabelingInferenceModelCatalog.IsSpatialPolicyCompatible(profileResult.Value, SelectedInferenceModel))
        {
            SetInferenceDialogError(
                LabelingInferenceModelCatalog.GetSpatialPolicyErrorMessage(profileResult.Value));
            return false;
        }

        if (!IsInferenceConfigValid)
        {
            SetInferenceDialogError("推理参数无效，请检查阈值与批次设置。");
            return false;
        }

        InferenceDialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasInferenceDialogErrorMessage));
        _inferenceSettingsBackup = null;
        return true;
    }

    private InferenceConfig BuildInferenceConfig() => ActiveSession.BuildConfig(CreateBuildContext());

    private InferenceDialogBuildContext CreateBuildContext()
    {
        var device = SelectedInferenceDevice?.Device ?? InferenceDevices.PrimaryGpu;
        if (!InferenceDevices.TryNormalize(device, out var normalizedDevice))
        {
            normalizedDevice = InferenceDevices.PrimaryGpu;
        }

        return new InferenceDialogBuildContext(
            SelectedInferenceModel,
            InferenceConfidence,
            InferenceMaxDetections,
            InferenceBatchSize,
            normalizedDevice,
            InferenceDeviceSummary,
            InferenceNmsIou);
    }

    private void RefreshInferenceDevices()
    {
        var previousDevice = SelectedInferenceDevice?.Device;
        InferenceDeviceOptions.Clear();

        InferenceDeviceOptions.Add(InferenceDeviceOption.Cpu);

        var gpuName = TryQueryPrimaryGpuName();
        InferenceDeviceOptions.Add(InferenceDeviceOption.PrimaryGpu(gpuName));

        SelectedInferenceDevice = InferenceDeviceOptions.FirstOrDefault(item =>
                string.Equals(item.Device, previousDevice, StringComparison.OrdinalIgnoreCase))
            ?? InferenceDeviceOptions.FirstOrDefault(item =>
                item.Device == InferenceDevices.PrimaryGpu)
            ?? InferenceDeviceOptions.FirstOrDefault();
        NotifyInferenceDeviceUi();
    }

    private void NotifyInferenceDeviceUi()
    {
        OnPropertyChanged(nameof(InferenceDeviceSummary));
    }

    private static string? TryQueryPrimaryGpuName()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name --format=csv,noheader",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2000);
            if (process.ExitCode != 0)
            {
                return null;
            }

            return output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0);
        }
        catch
        {
            return null;
        }
    }

    private void SetInferenceDialogError(string message)
    {
        InferenceDialogErrorMessage = message;
        OnPropertyChanged(nameof(HasInferenceDialogErrorMessage));
    }

    private void NotifyInferenceConfigState()
    {
        OnPropertyChanged(nameof(IsInferenceConfigValid));
        OnPropertyChanged(nameof(ShowInferenceConfigWarning));
        OnPropertyChanged(nameof(InferenceConfigSummary));
        OnPropertyChanged(nameof(InferenceDeviceSummary));
        OnPropertyChanged(nameof(InferenceSpatialPolicySummary));
        OnPropertyChanged(nameof(InferencePolicySectionTitle));
        OnPropertyChanged(nameof(InferenceButtonToolTip));
        OnPropertyChanged(nameof(InferenceActionLabel));
        OnPropertyChanged(nameof(IsClassificationModelSelected));
        OnPropertyChanged(nameof(ShowMaxDetectionsParameter));
        OnPropertyChanged(nameof(ShowBatchSizeParameter));
        OnPropertyChanged(nameof(InferenceDialogDescription));
        OnPropertyChanged(nameof(InferenceNoModelsHint));
        OnPropertyChanged(nameof(InferenceParametersFooterHint));
        OnPropertyChanged(nameof(ConfidenceThresholdHint));
        OnPropertyChanged(nameof(InferenceFixedBatchHint));
        OnPropertyChanged(nameof(HasInferenceFixedBatchHint));
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LabelingWorkspaceContext.WorkflowKind)
            or nameof(LabelingWorkspaceContext.IsClassificationProject)
            or nameof(LabelingWorkspaceContext.Project))
        {
            NotifyProjectWorkflowChanged();
            return;
        }

        if (e.PropertyName is nameof(LabelingWorkspaceContext.IsLoading)
            or nameof(LabelingWorkspaceContext.IsSaving)
            or nameof(LabelingWorkspaceContext.IsImporting)
            or nameof(LabelingWorkspaceContext.IsInferring)
            or nameof(LabelingWorkspaceContext.ImageSource)
            or nameof(LabelingWorkspaceContext.Gallery))
        {
            OnPropertyChanged(nameof(CanOpenInferenceDialog));
            NotifyInferenceCommandsCanExecute();
        }
    }

    public void NotifyProjectWorkflowChanged()
    {
        ApplyWorkflowDefaultThresholdsIfChanged();
        OnPropertyChanged(nameof(IsVisible));
        NotifyInferenceConfigState();
        OnPropertyChanged(nameof(CanOpenInferenceDialog));
        NotifyInferenceCommandsCanExecute();
    }

    private void ApplyWorkflowDefaultThresholdsIfChanged()
    {
        var isClassification = _context.IsClassificationProject;
        if (_lastIsClassificationProject == isClassification)
        {
            return;
        }

        _lastIsClassificationProject = isClassification;
        InferenceConfidence = isClassification ? 0.5 : 0.25;
    }

    private Result<ResolvedTaskProfile> TryResolveSelectedProfile()
    {
        var projectType = _context.Project?.Type;
        if (string.IsNullOrWhiteSpace(projectType))
        {
            return Result<ResolvedTaskProfile>.Failure("当前没有有效的项目类型。");
        }

        if (SelectedInferenceModel is null || string.IsNullOrWhiteSpace(SelectedInferenceModel.ModelType))
        {
            return Result<ResolvedTaskProfile>.Failure("请选择推理模型。");
        }

        return _taskCapabilities.ForProjectAndModel(projectType, SelectedInferenceModel.ModelType);
    }

    partial void OnSelectedInferenceModelChanged(InferenceModelOption? value)
    {
        RefreshInferenceDevices();
        NotifyInferenceConfigState();
    }

    partial void OnInferenceConfidenceChanged(double value) => NotifyInferenceConfigState();

    partial void OnInferenceNmsIouChanged(double value) => NotifyInferenceConfigState();

    partial void OnInferenceMaxDetectionsChanged(int value) => NotifyInferenceConfigState();

    partial void OnInferenceBatchSizeChanged(int value) => NotifyInferenceConfigState();

    partial void OnSelectedInferenceDeviceChanged(InferenceDeviceOption? value) => NotifyInferenceConfigState();

    private void NotifyInferenceCommandsCanExecute()
    {
        ShowAutoInferenceDialogCommand.NotifyCanExecuteChanged();
        RunAutoInferenceCommand.NotifyCanExecuteChanged();
        RunInferenceDebugCommand.NotifyCanExecuteChanged();
    }

    private sealed record InferenceSettingsSnapshot(
        InferenceModelOption? SelectedModel,
        double Confidence,
        double NmsIou,
        int MaxDetections,
        int BatchSize,
        InferenceDeviceOption? Device);
}
