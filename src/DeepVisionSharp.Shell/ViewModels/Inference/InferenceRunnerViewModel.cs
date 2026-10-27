using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels.Inference;

public sealed partial class InferenceRunnerViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly IInferenceRunner _inferenceRunner;
    private readonly ITaskCapabilityResolver _taskCapabilities;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<InferenceRunnerViewModel> _logger;

    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _warmupCts;
    private int _warmupGeneration;

    public InferenceRunnerViewModel(
        IMediator mediator,
        IInferenceRunner inferenceRunner,
        ITaskCapabilityResolver taskCapabilities,
        IUiDispatcher uiDispatcher,
        ShellSnackbarNotifier snackbar,
        ILogger<InferenceRunnerViewModel> logger)
    {
        _mediator = mediator;
        _inferenceRunner = inferenceRunner;
        _taskCapabilities = taskCapabilities;
        _uiDispatcher = uiDispatcher;
        _snackbar = snackbar;
        _logger = logger;

        DeviceOptions.Add(InferenceDeviceOption.Cpu);
        DeviceOptions.Add(InferenceDeviceOption.PrimaryGpu());
        SelectedDevice = DeviceOptions[^1];
    }

    public ObservableCollection<InferencePageModelOption> Models { get; } = new();

    public ObservableCollection<InferenceDeviceOption> DeviceOptions { get; } = new();

    [ObservableProperty]
    private InferencePageModelOption? _selectedModel;

    [ObservableProperty]
    private InferenceDeviceOption? _selectedDevice;

    [ObservableProperty]
    private double _confidenceThreshold = 0.25;

    [ObservableProperty]
    private int _maxDetections = InferenceMaxDetectionsLimits.Default;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isWarmingUp;

    [ObservableProperty]
    private string _warmupStatusText = string.Empty;

    [ObservableProperty]
    private int _selectedRightTabIndex;

    public bool ShowsConfidenceThreshold => true;

    public bool ShowsMaxDetections =>
        SelectedModel is not null
        && !ModelTypeNames.IsClassificationModelType(SelectedModel.ModelType);

    public string ResolveRunDevice(InferencePageModelOption? model)
    {
        if (SelectedDevice is not null && !string.IsNullOrWhiteSpace(SelectedDevice.DeviceValue))
        {
            return SelectedDevice.DeviceValue;
        }

        return InferenceDevices.PrimaryGpu;
    }

    /// <summary>按当前 TorchSharp 推理设备重建下拉。</summary>
    public void RefreshDeviceOptionsForModel(InferencePageModelOption? model)
    {
        var prevDevice = SelectedDevice?.DeviceValue;
        DeviceOptions.Clear();

        DeviceOptions.Add(InferenceDeviceOption.Cpu);
        DeviceOptions.Add(InferenceDeviceOption.PrimaryGpu());
        SelectedDevice = DeviceOptions.FirstOrDefault(d =>
                               string.Equals(d.DeviceValue, prevDevice, StringComparison.OrdinalIgnoreCase))
                         ?? DeviceOptions.FirstOrDefault(d =>
                               d.DeviceValue.StartsWith("cuda", StringComparison.OrdinalIgnoreCase))
                         ?? DeviceOptions[0];

        OnPropertyChanged(nameof(ShowsInferenceDeviceSelector));
    }

    public bool ShowsInferenceDeviceSelector => SelectedModel is not null;

    partial void OnSelectedModelChanged(InferencePageModelOption? value)
    {
        OnPropertyChanged(nameof(ShowsConfidenceThreshold));
        OnPropertyChanged(nameof(ShowsMaxDetections));
        RefreshDeviceOptionsForModel(value);
    }

    partial void OnMaxDetectionsChanged(int value)
    {
        var normalized = InferenceMaxDetectionsLimits.Normalize(value);
        if (normalized != value)
        {
            MaxDetections = normalized;
        }
    }

    public void ScheduleWarmup()
    {
        var gen = Interlocked.Increment(ref _warmupGeneration);
        _warmupCts?.Cancel();
        _warmupCts?.Dispose();

        var cts = new CancellationTokenSource();
        _warmupCts = cts;
        var token = cts.Token;

        var model = SelectedModel;

        if (model is null || string.IsNullOrWhiteSpace(model.ModelPath) || !File.Exists(model.ModelPath))
        {
            IsWarmingUp = false;
            WarmupStatusText = string.Empty;
            return;
        }

        IsWarmingUp = true;
        WarmupStatusText = "正在后台预热模型与算法引擎...";

        _ = Task.Run(async () =>
        {
            try
            {
                var runDevice = ResolveRunDevice(model);
                var config = new InferenceConfig
                {
                    ModelId = model.Id,
                    ConfidenceThreshold = Math.Clamp(ConfidenceThreshold, 0.01, 1.0),
                    MaxDetections = InferenceMaxDetectionsLimits.Normalize(MaxDetections),
                    BatchSize = 8,
                    Device = runDevice
                };

                var result = await _inferenceRunner.WarmupAsync(model.ModelPath, config, model.ModelType, token).ConfigureAwait(false);

                _uiDispatcher.Post(() =>
                {
                    if (gen == _warmupGeneration && !token.IsCancellationRequested)
                    {
                        IsWarmingUp = false;
                        WarmupStatusText = result.Success ? "模型预热完成" : $"模型预热失败：{result.Error ?? "未知错误"}";
                    }
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Warmup failed or skipped for {Model}", model.Name);
                _uiDispatcher.Post(() =>
                {
                    if (gen == _warmupGeneration && !token.IsCancellationRequested)
                    {
                        IsWarmingUp = false;
                        WarmupStatusText = "模型未预热（将在推理时自动载入）";
                    }
                });
            }
        }, token);
    }

    [RelayCommand]
    public async Task StopAsync()
    {
        _runCts?.Cancel();
        await _mediator.Send(new StopInferenceCommand("User cancelled from Inference page")).ConfigureAwait(true);
        // Native synchronous work cannot be interrupted safely. Its owning run clears IsRunning
        // only after it has returned; cancellation prevents publishing that late result.
    }

    /// <summary>
    /// Create a single shared cancellation token for「推理全部」.
    /// <see cref="StopAsync"/> and <see cref="CancelRuns"/> cancel this token so the whole batch
    /// stops together.
    /// </summary>
    public CancellationToken BeginSharedRun()
    {
        _runCts?.Cancel();
        _runCts?.Dispose();
        _runCts = new CancellationTokenSource();
        return _runCts.Token;
    }

    /// <summary>
    /// Cancels in-flight and warm-up work without sending a command. Used when the page goes away:
    /// the batch must not keep running against a view model nobody is watching.
    /// </summary>
    public void CancelRuns()
    {
        try
        {
            _runCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        try
        {
            _warmupCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <param name="manageIsRunning">
    /// When true (default), toggles <see cref="IsRunning"/> around this call.
    /// Batch callers that already hold the running flag must pass false to avoid per-item UI thrashing.
    /// </param>
    /// <param name="showErrorSnackbar">
    /// When false, failures are recorded on the item only (batch mode uses a single end summary).
    /// </param>
    public async Task<bool> RunOneAsync(
        InferencePageModelOption model,
        InferenceImageItem image,
        Action<InferenceResultSummary, object?> applyResult,
        Action clearResult,
        Action<string> setStatusText,
        CancellationToken ct = default,
        bool manageIsRunning = true,
        bool showErrorSnackbar = true)
    {
        if (manageIsRunning && IsRunning) return false;
        var localCt = ct;
        if (localCt == default)
        {
            if (manageIsRunning)
            {
                // Single-shot run: own a CTS so Stop can cancel this infer only.
                _runCts?.Cancel();
                _runCts?.Dispose();
                _runCts = new CancellationTokenSource();
                localCt = _runCts.Token;
            }
            else
            {
                // Batch mode without an explicit token must NOT cancel sibling workers
                // by recreating the shared _runCts on every item (that caused ~99% fails).
                localCt = CancellationToken.None;
            }
        }

        try
        {
            if (manageIsRunning)
            {
                await _uiDispatcher.InvokeAsync(() => IsRunning = true).ConfigureAwait(false);
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                setStatusText($"正在推理：{image.DisplayName}");
                image.LastInferError = null;
                image.PredictedClass = null;
                image.PredictedConfidence = null;
                image.LastInferElapsedMs = null;
                image.LastPerformanceText = null;
                image.LastResultSummary = null;
                clearResult();
            }).ConfigureAwait(false);

            var config = new InferenceConfig
            {
                ModelId = model.Id,
                ConfidenceThreshold = Math.Clamp(ConfidenceThreshold, 0.01, 1.0),
                MaxDetections = InferenceMaxDetectionsLimits.Normalize(MaxDetections),
                BatchSize = 8,
                Device = ResolveRunDevice(model)
            };

            if (string.IsNullOrWhiteSpace(model.ModelPath) || !File.Exists(model.ModelPath))
            {
                var missing = "模型权重文件不存在。";
                await _uiDispatcher.InvokeAsync(() =>
                {
                    image.LastInferError = missing;
                    if (showErrorSnackbar)
                    {
                        _snackbar.ShowError(missing, "推理");
                    }
                }).ConfigureAwait(false);
                return false;
            }

            var sw = Stopwatch.StartNew();
            // Stay off the UI thread for the actual inference work.
            var runResult = await _inferenceRunner
                .RunSingleAsync(model.ModelPath, image.ImagePath, config, model.ModelType, localCt)
                .ConfigureAwait(false);
            sw.Stop();
            localCt.ThrowIfCancellationRequested();

            if (!runResult.Success)
            {
                var err = runResult.Error ?? "推理服务失败";
                _logger.LogError(
                    "Inference failed for {File}: {Error}",
                    image.DisplayName,
                    err);
                await _uiDispatcher.InvokeAsync(() =>
                {
                    image.LastInferError = err;
                    if (showErrorSnackbar)
                    {
                        _snackbar.ShowError(err, "推理");
                    }
                }).ConfigureAwait(false);
                return false;
            }

            var summary = InferenceResultSummary.FromResult(runResult, model.ModelType);
            var (predicted, confidence) = InferenceOkNgPrediction.TryResolve(runResult, model.ModelType);
            var elapsedMs = sw.Elapsed.TotalMilliseconds;

            await _uiDispatcher.InvokeAsync(() =>
            {
                localCt.ThrowIfCancellationRequested();
                image.LastInferElapsedMs = elapsedMs;
                image.LastPerformanceText = summary.PerformanceText;
                applyResult(summary, runResult);
                image.PredictedClass = predicted;
                image.PredictedConfidence = confidence;
            }).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException)
        {
            await _uiDispatcher.InvokeAsync(() => image.LastInferError = "已取消").ConfigureAwait(false);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunOneAsync failed for {File}", image.DisplayName);
            await _uiDispatcher.InvokeAsync(() =>
            {
                image.LastInferError = ex.Message;
                if (showErrorSnackbar)
                {
                    _snackbar.ShowError(ex.Message, "推理");
                }
            }).ConfigureAwait(false);
            return false;
        }
        finally
        {
            if (manageIsRunning)
            {
                await _uiDispatcher.InvokeAsync(() => IsRunning = false).ConfigureAwait(false);
            }
        }
    }
}
