using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Labeling;
using DeepVisionSharp.Shell.Services.Tasks;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public sealed record GalleryBatchInferenceScopeOption(GalleryBatchInferenceScope Scope, string Label)
{
    public override string ToString() => Label;
}

public sealed record GalleryBatchInferenceWriteBackOption(
    GalleryBatchInferenceWriteBackPolicy Policy,
    string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Gallery-card 「图库推理…」 dialog: configure model / scope / write-back, then run batch inference in-dialog.
/// </summary>
public sealed partial class GalleryBatchInferenceViewModel : ObservableObject
{
    /// <summary>Task-center log lines are written every N samples, not for every sample.</summary>
    private const int ProgressLogInterval = 25;

    private readonly IMediator _mediator;
    private readonly ITaskCapabilityResolver _taskCapabilities;
    private readonly IAppDialogService _appDialogService;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ShellTaskCenterViewModel _taskCenter;
    private readonly GalleryListViewModel _list;
    private readonly ILogger<GalleryBatchInferenceViewModel> _logger;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _prepareCts;
    private Guid? _backgroundTaskId;
    private string? _cachedGpuName;

    public GalleryBatchInferenceViewModel(
        IMediator mediator,
        ITaskCapabilityResolver taskCapabilities,
        IAppDialogService appDialogService,
        ShellSnackbarNotifier snackbar,
        ShellTaskCenterViewModel taskCenter,
        GalleryListViewModel list,
        ILogger<GalleryBatchInferenceViewModel> logger)
    {
        _mediator = mediator;
        _taskCapabilities = taskCapabilities;
        _appDialogService = appDialogService;
        _snackbar = snackbar;
        _taskCenter = taskCenter;
        _list = list;
        _logger = logger;

        ScopeOptions =
        [
            new GalleryBatchInferenceScopeOption(GalleryBatchInferenceScope.All, "全部样本"),
            new GalleryBatchInferenceScopeOption(GalleryBatchInferenceScope.UnlabeledOnly, "仅未标注"),
            new GalleryBatchInferenceScopeOption(GalleryBatchInferenceScope.LabeledOnly, "仅已标注")
        ];
        SelectedScopeOption = ScopeOptions[0];

        WriteBackOptions =
        [
            new GalleryBatchInferenceWriteBackOption(
                GalleryBatchInferenceWriteBackPolicy.SkipExisting,
                "跳过已有标注（推荐）"),
            // Whole-image classification stores exactly one class per sample, so "append" has nothing
            // to add and would silently overwrite. It is not offered as a separate choice.
            new GalleryBatchInferenceWriteBackOption(
                GalleryBatchInferenceWriteBackPolicy.Replace,
                "覆盖已有标注（危险）")
        ];
        SelectedWriteBackOption = WriteBackOptions[0];
    }

    public ObservableCollection<InferenceModelOption> Models { get; } = new();

    public ObservableCollection<InferenceDeviceOption> DeviceOptions { get; } = new();

    public IReadOnlyList<GalleryBatchInferenceScopeOption> ScopeOptions { get; }

    public IReadOnlyList<GalleryBatchInferenceWriteBackOption> WriteBackOptions { get; }

    [ObservableProperty]
    private InferenceModelOption? _selectedModel;

    [ObservableProperty]
    private InferenceDeviceOption? _selectedDevice;

    [ObservableProperty]
    private GalleryBatchInferenceScopeOption? _selectedScopeOption;

    [ObservableProperty]
    private GalleryBatchInferenceWriteBackOption? _selectedWriteBackOption;

    [ObservableProperty]
    private double _confidence = 0.25;

    [ObservableProperty]
    private int _maxDetections = InferenceMaxDetectionsLimits.Default;

    [ObservableProperty]
    private string _galleryName = string.Empty;

    [ObservableProperty]
    private string _projectTypeLabel = string.Empty;

    [ObservableProperty]
    private string _galleryMetaText = string.Empty;

    [ObservableProperty]
    private string _scopeHint = string.Empty;

    [ObservableProperty]
    private string _writeBackHint = "写回后可在标注页逐张核对。大批量任务会进入后台任务中心，可中途取消。";

    [ObservableProperty]
    private string _noModelsHint = string.Empty;

    [ObservableProperty]
    private bool _hasModels;

    public bool HasNoModels => !HasModels;

    [ObservableProperty]
    private bool _isPreparing;

    [ObservableProperty]
    private string _preparingStatusText = "正在准备…";

    [ObservableProperty]
    private bool _isConfigView = true;

    [ObservableProperty]
    private bool _isRunningView;

    [ObservableProperty]
    private bool _isDoneView;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _stageText = string.Empty;

    [ObservableProperty]
    private string _progressCountText = string.Empty;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _currentFileText = "—";

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private int _resultTotal;

    [ObservableProperty]
    private int _resultWritten;

    [ObservableProperty]
    private int _resultSkipped;

    [ObservableProperty]
    private int _resultFailed;

    public bool IsNotRunning => !IsRunning;

    public bool IsConfigInteractive => IsConfigView && !IsPreparing && !IsRunning;

    public bool CanStart =>
        IsConfigView
        && !IsPreparing
        && HasModels
        && SelectedModel is not null
        && SelectedDevice is not null
        && !IsRunning;

    public bool ShowsMaxDetections =>
        SelectedModel is not null
        && !ModelTypeNames.IsClassificationModelType(SelectedModel.ModelType);

    public string Subtitle => IsPreparing
        ? "正在准备模型与设备列表，请稍候…"
        : IsConfigView
            ? "使用模型库模型对整库样本批量推理并写回标注。"
            : IsRunningView
                ? "任务进行中，关闭窗口不会中断（将转入任务中心）。"
                : "本轮推理已结束，可进入标注页核对结果。";

    private Guid _galleryId;
    private Guid _projectId;
    private string _projectType = string.Empty;
    private int _sampleCount;
    private int _annotatedCount;
    private InferenceWriteBackKind _writeBackKind = InferenceWriteBackKind.None;

    [RelayCommand(CanExecute = nameof(CanShowDialog))]
    private async Task ShowDialogAsync(Guid galleryId)
    {
        var gallery = _list.Galleries.FirstOrDefault(item => item.Id == galleryId);
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到图库。", "图库推理");
            return;
        }

        // Open dialog immediately with a loading overlay; heavy prep runs in parallel.
        BeginPrepare(gallery);
        var prepareTask = PrepareAsync(gallery, _prepareCts!.Token);

        try
        {
            await _appDialogService.ShowAsync(
                AppDialogKind.GalleryBatchInference,
                this,
                new DialogOptions(
                    Title: "图库推理",
                    PrimaryButtonText: "关闭",
                    CloseButtonText: string.Empty));
        }
        finally
        {
            _prepareCts?.Cancel();
            try
            {
                await prepareTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                // Dialog closed while still preparing.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gallery batch inference prepare failed after dialog closed");
            }

            if (IsRunning)
            {
                AppendLogLine("对话框已关闭，任务继续在后台运行。");
            }
            else
            {
                await CancelAndWaitAsync();
            }

            IsPreparing = false;
        }

        await _list.RefreshCurrentProjectGalleriesAsync();
        await _list.RecalculateProjectProgressAsync();
    }

    private bool CanShowDialog() => _list.CanRunGalleryOperation() && !IsPreparing && !IsRunning;

    private void BeginPrepare(GalleryRow gallery)
    {
        _prepareCts?.Cancel();
        _prepareCts?.Dispose();
        _prepareCts = new CancellationTokenSource();

        ResetUiState();
        _galleryId = gallery.Id;
        _projectId = gallery.ProjectId;
        _projectType = _list.CurrentProjectType;
        _sampleCount = gallery.SampleCount;
        _annotatedCount = gallery.AnnotatedCount;

        GalleryName = gallery.Name;
        ProjectTypeLabel = ProjectTypes.GetDisplayLabel(_projectType);
        GalleryMetaText =
            $"{gallery.SampleCount} 样本 · {gallery.AnnotatedCount} 已标注 · {Math.Max(0, gallery.SampleCount - gallery.AnnotatedCount)} 未标注";

        // Immediate device options so the form is never empty while GPU probe runs.
        DeviceOptions.Clear();
        DeviceOptions.Add(InferenceDeviceOption.Cpu);
        DeviceOptions.Add(InferenceDeviceOption.PrimaryGpu());
        SelectedDevice = DeviceOptions.FirstOrDefault(item => item.Device == InferenceDevices.PrimaryGpu)
            ?? DeviceOptions.FirstOrDefault();

        IsPreparing = true;
        PreparingStatusText = "正在加载模型与设备…";
        UpdateScopeHint();
        UpdateWriteBackHint();
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(IsConfigInteractive));
        StartCommand.NotifyCanExecuteChanged();
        ShowDialogCommand.NotifyCanExecuteChanged();
    }

    private async Task PrepareAsync(GalleryRow gallery, CancellationToken cancellationToken)
    {
        try
        {
            PreparingStatusText = "正在探测 GPU 并加载模型列表…";

            var devicesTask = RefreshDevicesAsync(cancellationToken);
            var modelsTask = LoadModelsAsync(cancellationToken);
            await Task.WhenAll(devicesTask, modelsTask).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            PreparingStatusText = "准备完成";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to prepare gallery batch inference for {GalleryId}", gallery.Id);
            NoModelsHint = string.IsNullOrWhiteSpace(NoModelsHint)
                ? $"准备失败：{ex.Message}"
                : NoModelsHint;
            if (Models.Count == 0)
            {
                HasModels = false;
            }
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                IsPreparing = false;
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(IsConfigInteractive));
                StartCommand.NotifyCanExecuteChanged();
                ShowDialogCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private void ResetUiState()
    {
        IsConfigView = true;
        IsRunningView = false;
        IsDoneView = false;
        IsRunning = false;
        IsPreparing = false;
        PreparingStatusText = "正在准备…";
        StageText = string.Empty;
        ProgressCountText = string.Empty;
        ProgressValue = 0;
        CurrentFileText = "—";
        LogText = string.Empty;
        ResultTotal = ResultWritten = ResultSkipped = ResultFailed = 0;
        Confidence = 0.25;
        MaxDetections = InferenceMaxDetectionsLimits.Default;
        SelectedScopeOption = ScopeOptions[0];
        SelectedWriteBackOption = WriteBackOptions[0];
        Models.Clear();
        SelectedModel = null;
        HasModels = false;
        NoModelsHint = string.Empty;
        _writeBackKind = InferenceWriteBackKind.None;
        _backgroundTaskId = null;
    }

    private async Task LoadModelsAsync(CancellationToken cancellationToken)
    {
        Models.Clear();
        SelectedModel = null;
        HasModels = false;
        NoModelsHint = string.Empty;

        if (!ProjectTypes.IsValid(_projectType))
        {
            NoModelsHint = "当前项目类型无效，无法加载推理模型。";
            return;
        }

        PreparingStatusText = "正在读取模型库…";
        var modelsResult = await _mediator.Send(new GetModelsByProjectQuery(_projectId), cancellationToken);
        if (modelsResult.IsFailure || modelsResult.Value is null)
        {
            NoModelsHint = modelsResult.Error ?? "加载模型列表失败。";
            return;
        }

        var projectResult = await _mediator.Send(new GetProjectByIdQuery(_projectId), cancellationToken);
        var projectLabels = projectResult.Value?.Labels ?? Array.Empty<string>();

        // Cheap profile/write-back filter first (no disk I/O).
        var profileCandidates = new List<(
            Guid Id,
            string Name,
            string ModelType,
            string CheckpointPath,
            IReadOnlyList<string>? Classes)>();
        foreach (var model in modelsResult.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(model.CheckpointPath)
                || string.IsNullOrWhiteSpace(model.ModelType)
                || !LabelingInferenceModelCatalog.IsInferenceEligibleStatus(model.Status))
            {
                continue;
            }

            var profileResult = _taskCapabilities.ForProjectAndModel(_projectType, model.ModelType);
            if (profileResult.IsFailure
                || profileResult.Value is null
                || profileResult.Value.WriteBackKind is InferenceWriteBackKind.None)
            {
                continue;
            }

            if (profileResult.Value.WriteBackKind is InferenceWriteBackKind.ImageClassTop1)
            {
                if (!LabelingInferenceModelCatalog.ClassSetsMatch(model.Classes, projectLabels))
                {
                    continue;
                }
            }

            profileCandidates.Add((model.Id, model.Name, model.ModelType, model.CheckpointPath, model.Classes));
        }

        PreparingStatusText = profileCandidates.Count == 0
            ? "未找到兼容模型"
            : $"正在校验模型文件（{profileCandidates.Count}）…";

        // Disk existence checks off the UI thread (slow on network/NAS paths).
        var existingCandidates = await Task.Run(
            () => profileCandidates
                .Where(item => File.Exists(item.CheckpointPath))
                .ToList(),
            cancellationToken).ConfigureAwait(true);

        cancellationToken.ThrowIfCancellationRequested();
        PreparingStatusText = existingCandidates.Count == 0
            ? "模型文件不可用"
            : $"正在读取模型元数据（{existingCandidates.Count}）…";

        // Metadata reads in parallel — previously sequential and often the slowest open-dialog step.
        var metadataTasks = existingCandidates.Select(async model =>
        {
            var metadataResult = await _mediator.Send(
                new GetModelDisplayMetricsQuery(model.CheckpointPath),
                cancellationToken).ConfigureAwait(false);
            return new InferenceModelOption(
                model.Id,
                model.Name,
                model.ModelType,
                model.CheckpointPath,
                metadataResult.IsSuccess ? metadataResult.Value?.InputSize : null);
        });

        var loaded = (await Task.WhenAll(metadataTasks).ConfigureAwait(true)).ToList();

        loaded = loaded
            .OrderByDescending(item => ModelTypeNames.IsClassificationModelType(item.ModelType))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        cancellationToken.ThrowIfCancellationRequested();
        Models.Clear();
        foreach (var model in loaded)
        {
            Models.Add(model);
        }

        SelectedModel = Models.FirstOrDefault();
        RefreshDevicesForSelectedModel();
        HasModels = Models.Count > 0;
        if (!HasModels)
        {
            NoModelsHint = "模型库中没有与当前项目兼容且可推理的模型。请先训练或导入模型。";
        }

        ResolveWriteBackKind();
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshDevicesAsync(CancellationToken cancellationToken)
    {
        _cachedGpuName = await Task.Run(TryQueryPrimaryGpuName, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        RefreshDevicesForSelectedModel();
    }

    private void RefreshDevicesForSelectedModel()
    {
        var previous = SelectedDevice?.Device;
        DeviceOptions.Clear();
        DeviceOptions.Add(InferenceDeviceOption.Cpu);
        DeviceOptions.Add(InferenceDeviceOption.PrimaryGpu(_cachedGpuName));
        SelectedDevice = DeviceOptions.FirstOrDefault(item =>
                             string.Equals(item.Device, previous, StringComparison.OrdinalIgnoreCase))
                         ?? DeviceOptions.FirstOrDefault(item => item.Device == InferenceDevices.PrimaryGpu)
                         ?? DeviceOptions.FirstOrDefault();
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

            if (!process.WaitForExit(800))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort; GPU label falls back to generic.
                }

                return null;
            }

            var output = process.StandardOutput.ReadLine();
            return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
        }
        catch
        {
            return null;
        }
    }

    public bool ShowsSelectedModelSpatialSummary =>
        !string.IsNullOrWhiteSpace(SelectedModel?.SpatialPolicySummary);

    partial void OnSelectedModelChanged(InferenceModelOption? value)
    {
        ResolveWriteBackKind();
        RefreshDevicesForSelectedModel();
        OnPropertyChanged(nameof(ShowsMaxDetections));
        OnPropertyChanged(nameof(ShowsSelectedModelSpatialSummary));
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }

    partial void OnMaxDetectionsChanged(int value)
    {
        var normalized = InferenceMaxDetectionsLimits.Normalize(value);
        if (normalized != value)
        {
            MaxDetections = normalized;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (IsPreparing
            || SelectedModel is null
            || SelectedDevice is null
            || SelectedScopeOption is null
            || SelectedWriteBackOption is null)
        {
            return;
        }

        ResolveWriteBackKind();
        if (_writeBackKind is InferenceWriteBackKind.None)
        {
            _snackbar.ShowWarning("所选模型不支持图库批量写回。", "图库推理");
            return;
        }

        var profileResult = _taskCapabilities.ForProjectAndModel(_projectType, SelectedModel.ModelType);
        if (profileResult.IsFailure || profileResult.Value is null)
        {
            _snackbar.ShowWarning(profileResult.Error ?? "模型与项目不兼容。", "图库推理");
            return;
        }

        if (!LabelingInferenceModelCatalog.IsSpatialPolicyCompatible(profileResult.Value, SelectedModel))
        {
            _snackbar.ShowWarning(
                LabelingInferenceModelCatalog.GetSpatialPolicyErrorMessage(profileResult.Value),
                "图库推理");
            return;
        }

        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        ShowView(running: true);
        LogText = string.Empty;
        StageText = "正在启动…";
        ProgressCountText = "准备中";
        ProgressValue = 0;
        CurrentFileText = "首张样本会启动推理引擎并加载模型，可能需要数十秒";
        AppendLogLine(
            $"[start] model={SelectedModel.DisplayText} device={SelectedDevice.Device} conf={Confidence:F2} max_det={InferenceMaxDetectionsLimits.Normalize(MaxDetections)}");
        AppendLogLine($"[scope] {SelectedScopeOption.Scope} · writeback={SelectedWriteBackOption.Policy}");
        AppendLogLine("[hint] 首次加载运行时与模型权重可能较慢，进度条将在首张推理开始后推进");

        _backgroundTaskId = _taskCenter.StartTask(
            BackgroundTaskKind.GalleryBatchInference,
            $"图库推理 · {GalleryName}");
        _taskCenter.AppendLog(_backgroundTaskId.Value, $"开始：{SelectedModel.Name}");
        _taskCenter.AppendLog(_backgroundTaskId.Value, "准备样本列表；首张将加载推理引擎…");

        var progress = new Progress<GalleryBatchInferenceProgress>(OnProgress);
        try
        {
            var result = await _mediator.Send(
                new RunGalleryBatchInferenceCommand(
                    _projectId,
                    _galleryId,
                    SelectedModel.Id,
                    SelectedModel.CheckpointPath,
                    SelectedModel.ModelType,
                    _writeBackKind,
                    SelectedScopeOption.Scope,
                    SelectedWriteBackOption.Policy,
                    Confidence,
                    SelectedDevice.Device,
                    InferenceMaxDetectionsLimits.Normalize(MaxDetections),
                    progress),
                _runCts.Token);

            if (result.IsFailure || result.Value is null)
            {
                AppendLogLine($"[error] {result.Error ?? "推理失败"}");
                if (_backgroundTaskId is { } failId)
                {
                    _taskCenter.CompleteTask(failId, success: false, result.Error);
                }

                _snackbar.ShowError(result.Error ?? "图库推理失败。", "图库推理");
                ShowView(done: true);
                return;
            }

            ApplyResult(result.Value);
            foreach (var error in result.Value.Errors.Take(8))
            {
                AppendLogLine($"[fail] {error}");
            }

            AppendLogLine(
                $"[done] wrote={result.Value.Written} skipped={result.Value.Skipped} failed={result.Value.Failed}");
            if (_backgroundTaskId is { } okId)
            {
                _taskCenter.CompleteTask(okId, success: result.Value.Failed == 0 || result.Value.Written > 0);
            }

            _snackbar.ShowSuccess(
                $"图库推理完成：写回 {result.Value.Written}，跳过 {result.Value.Skipped}，失败 {result.Value.Failed}",
                "图库推理");
            ShowView(done: true);
        }
        catch (OperationCanceledException)
        {
            AppendLogLine("[cancel] 用户取消任务");
            if (_backgroundTaskId is { } cancelId)
            {
                _taskCenter.CompleteTask(cancelId, success: false, "已取消");
            }

            _snackbar.ShowInfo("已取消图库推理。", "图库推理");
            ShowView(done: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gallery batch inference failed for gallery {GalleryId}", _galleryId);
            AppendLogLine($"[error] {ex.Message}");
            if (_backgroundTaskId is { } errId)
            {
                _taskCenter.CompleteTask(errId, success: false, ex.Message);
            }

            _snackbar.ShowError($"图库推理失败：{ex.Message}", "图库推理");
            ShowView(done: true);
        }
        finally
        {
            IsRunning = false;
            // StopInferenceCommand is process-wide: it stops every inference backend, not just this
            // batch. Sending it on normal completion would kill a labeling-page auto-inference the
            // user started in parallel (today the in-process backend's StopAsync is a no-op, but the
            // command's contract is global). The batch's own token already cancelled its work.
            OnPropertyChanged(nameof(IsNotRunning));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(IsConfigInteractive));
            StartCommand.NotifyCanExecuteChanged();
            CancelRunCommand.NotifyCanExecuteChanged();
            ShowDialogCommand.NotifyCanExecuteChanged();
            _list.NotifyBusyStateChanged();

            // The dialog may be closed while the run keeps going in the background. Refresh here
            // so gallery counters and project progress do not stay stale until the next navigation.
            try
            {
                await _list.RefreshCurrentProjectGalleriesAsync();
                await _list.RecalculateProjectProgressAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Gallery refresh after batch inference failed");
            }
        }
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private async Task CancelRunAsync()
    {
        if (_runCts is null || _runCts.IsCancellationRequested)
        {
            return;
        }

        StageText = "正在取消…";
        await _runCts.CancelAsync();
        try
        {
            await _mediator.Send(new StopInferenceCommand("User cancelled gallery batch"));
        }
        catch
        {
            // ignore
        }
    }

    private async Task CancelAndWaitAsync()
    {
        if (_runCts is { IsCancellationRequested: false })
        {
            await _runCts.CancelAsync();
        }
    }

    private void OnProgress(GalleryBatchInferenceProgress value)
    {
        StageText = value.Stage;
        ProgressCountText = value.Total > 0 ? $"{value.Processed} / {value.Total}" : "准备中";
        ProgressValue = value.Total > 0
            ? Math.Clamp((double)value.Processed / value.Total, 0, 1)
            : 0;
        CurrentFileText = string.IsNullOrWhiteSpace(value.CurrentFile) ? "—" : value.CurrentFile;

        if (_backgroundTaskId is { } stageTaskId && value.Total == 0)
        {
            _taskCenter.AppendLog(stageTaskId, value.Stage);
            return;
        }

        if (_backgroundTaskId is not { } taskId || string.IsNullOrWhiteSpace(value.CurrentFile))
        {
            return;
        }

        // One task-center line per sample means a 5000-image gallery queues 5000 dispatcher posts
        // and trims the bounded log on every one of them. Report at most every
        // ProgressLogInterval samples, plus the final one.
        var isFinal = value.Total > 0 && value.Processed >= value.Total;
        if (!isFinal
            && value.Processed > 0
            && value.Processed % ProgressLogInterval != 0)
        {
            return;
        }

        _taskCenter.AppendLog(taskId, $"{value.Processed}/{value.Total} {value.CurrentFile}");
    }

    private void ApplyResult(GalleryBatchInferenceResult result)
    {
        ResultTotal = result.Total;
        ResultWritten = result.Written;
        ResultSkipped = result.Skipped;
        ResultFailed = result.Failed;
        ProgressValue = 1;
        ProgressCountText = $"{result.Total} / {result.Total}";
        StageText = "已完成";
    }

    private void ShowView(bool running = false, bool done = false)
    {
        IsConfigView = !running && !done;
        IsRunningView = running;
        IsDoneView = done;
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(IsConfigInteractive));
        StartCommand.NotifyCanExecuteChanged();
        CancelRunCommand.NotifyCanExecuteChanged();
    }

    private void ResolveWriteBackKind()
    {
        _writeBackKind = InferenceWriteBackKind.None;
        if (SelectedModel is null || !ProjectTypes.IsValid(_projectType))
        {
            return;
        }

        var profile = _taskCapabilities.ForProjectAndModel(_projectType, SelectedModel.ModelType);
        if (profile.IsSuccess && profile.Value is not null)
        {
            _writeBackKind = profile.Value.WriteBackKind;
        }
    }

    private void UpdateScopeHint()
    {
        if (SelectedScopeOption is null)
        {
            ScopeHint = string.Empty;
            return;
        }

        var unlabeled = Math.Max(0, _sampleCount - _annotatedCount);
        var (count, label) = SelectedScopeOption.Scope switch
        {
            GalleryBatchInferenceScope.UnlabeledOnly => (unlabeled, "仅未标注"),
            GalleryBatchInferenceScope.LabeledOnly => (_annotatedCount, "仅已标注"),
            _ => (_sampleCount, "全部")
        };
        ScopeHint = $"预计处理 {count} 张（{label}）。";
    }

    private void UpdateWriteBackHint()
    {
        WriteBackHint = SelectedWriteBackOption?.Policy switch
        {
            GalleryBatchInferenceWriteBackPolicy.Replace =>
                "将覆盖已有人工标注，此操作不可撤销。建议先导出图库备份。",
            _ => "已有标注的样本将被跳过。写回后可在标注页逐张核对。"
        };
    }

    private void AppendLogLine(string line)
    {
        LogText = string.IsNullOrEmpty(LogText) ? line : LogText + Environment.NewLine + line;
    }

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotRunning));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(IsConfigInteractive));
        StartCommand.NotifyCanExecuteChanged();
        CancelRunCommand.NotifyCanExecuteChanged();
        ShowDialogCommand.NotifyCanExecuteChanged();
        _list.NotifyBusyStateChanged();
    }

    partial void OnIsPreparingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(IsConfigInteractive));
        OnPropertyChanged(nameof(Subtitle));
        StartCommand.NotifyCanExecuteChanged();
        ShowDialogCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedScopeOptionChanged(GalleryBatchInferenceScopeOption? value) => UpdateScopeHint();

    partial void OnSelectedWriteBackOptionChanged(GalleryBatchInferenceWriteBackOption? value) =>
        UpdateWriteBackHint();

    partial void OnIsConfigViewChanged(bool value)
    {
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(IsConfigInteractive));
        StartCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasModelsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoModels));
        OnPropertyChanged(nameof(CanStart));
        StartCommand.NotifyCanExecuteChanged();
    }
}
