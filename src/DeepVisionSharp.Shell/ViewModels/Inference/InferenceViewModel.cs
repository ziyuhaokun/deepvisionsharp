using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Media;
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
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.ViewModels.Inference;

/// <summary>
/// Inference page coordinator root. Feature logic lives in Worklist / Canvas / Runner / Report.
/// </summary>
public sealed partial class InferenceViewModel : ObservableObject, IDisposable
{
    private readonly IMediator _mediator;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly ITaskCapabilityResolver _taskCapabilities;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly IProjectLabelService _projectLabels;
    private readonly IAppDialogService _appDialogService;
    private readonly IImageSourceFactory _imageSources;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<InferenceViewModel> _logger;

    private CancellationTokenSource? _loadCts;
    private bool _disposed;
    private bool _isActive;

    public InferenceViewModel(
        IMediator mediator,
        IInferenceRunner inferenceRunner,
        IWarmupCoordinator warmups,
        WorkspaceSessionService workspaceSession,
        ILocalFileDialogService fileDialog,
        IProjectFileStore fileStore,
        IImageSourceFactory imageSources,
        ITaskCapabilityResolver taskCapabilities,
        IAnnotationWorkflowService annotations,
        IProjectLabelService projectLabels,
        IAppDialogService appDialogService,
        IClipboardService clipboard,
        IUiDispatcher uiDispatcher,
        ShellSnackbarNotifier snackbar,
        ILogger<InferenceViewModel> logger,
        ILogger<InferenceWorklistViewModel> worklistLogger,
        ILogger<InferenceRunnerViewModel> runnerLogger)
    {
        _mediator = mediator;
        _workspaceSession = workspaceSession;
        _taskCapabilities = taskCapabilities;
        _annotations = annotations;
        _projectLabels = projectLabels;
        _appDialogService = appDialogService;
        _imageSources = imageSources;
        _uiDispatcher = uiDispatcher;
        _snackbar = snackbar;
        _logger = logger;

        Worklist = new InferenceWorklistViewModel(
            mediator,
            fileDialog,
            fileStore,
            imageSources,
            workspaceSession,
            snackbar,
            worklistLogger);

        Canvas = new InferenceCanvasViewModel();

        Runner = new InferenceRunnerViewModel(
            mediator,
            inferenceRunner,
            taskCapabilities,
            uiDispatcher,
            snackbar,
            warmups,
            runnerLogger);

        Report = new InferenceReportViewModel(
            appDialogService,
            clipboard,
            fileDialog,
            imageSources,
            snackbar);

        // Wire property changed notifications and cross-sub-ViewModel interactions
        Worklist.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InferenceWorklistViewModel.SelectedImage))
            {
                OnPropertyChanged(nameof(SelectedImage));
                OnSelectedImageChanged(Worklist.SelectedImage);
            }
            else if (e.PropertyName == nameof(InferenceWorklistViewModel.HasProjectContext)
                || e.PropertyName == nameof(InferenceWorklistViewModel.ProjectTitle)
                || e.PropertyName == nameof(InferenceWorklistViewModel.StatusText)
                || e.PropertyName == nameof(InferenceWorklistViewModel.IsLoading)
                || e.PropertyName == nameof(InferenceWorklistViewModel.SourceMode)
                || e.PropertyName == nameof(InferenceWorklistViewModel.SelectedGallery)
                || e.PropertyName == nameof(InferenceWorklistViewModel.ImageCount)
                || e.PropertyName == nameof(InferenceWorklistViewModel.ImageSourceSummary)
                || e.PropertyName == nameof(InferenceWorklistViewModel.IsExternalSource)
                || e.PropertyName == nameof(InferenceWorklistViewModel.IsGallerySource))
            {
                OnPropertyChanged(e.PropertyName);
                NotifyStatusBar();
            }
        };

        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InferenceCanvasViewModel.PreviewImage)
                || e.PropertyName == nameof(InferenceCanvasViewModel.SelectedResultRow)
                || e.PropertyName == nameof(InferenceCanvasViewModel.ActiveFileName)
                || e.PropertyName == nameof(InferenceCanvasViewModel.ImageSizeText)
                || e.PropertyName == nameof(InferenceCanvasViewModel.HasPreviewImage)
                || e.PropertyName == nameof(InferenceCanvasViewModel.ShowResultRowList))
            {
                OnPropertyChanged(e.PropertyName);
                NotifyStatusBar();
            }
        };

        Runner.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(InferenceRunnerViewModel.SelectedModel))
            {
                OnPropertyChanged(nameof(SelectedModel));
                OnPropertyChanged(nameof(ShowsConfidenceThreshold));
                OnPropertyChanged(nameof(ShowsMaxDetections));
                OnPropertyChanged(nameof(ShowsInferenceDeviceSelector));
                // Device list rebuild + warmup are handled in Runner.OnSelectedModelChanged.
                NotifyCanRun();
                NotifyStatusBar();
            }
            else if (e.PropertyName == nameof(InferenceRunnerViewModel.SelectedDevice))
            {
                OnPropertyChanged(nameof(SelectedDevice));
                NotifyCanRun();
                Runner.ScheduleWarmup();
                NotifyStatusBar();
            }
            else if (e.PropertyName == nameof(InferenceRunnerViewModel.IsRunning)
                || e.PropertyName == nameof(InferenceRunnerViewModel.IsWarmingUp)
                || e.PropertyName == nameof(InferenceRunnerViewModel.WarmupStatusText)
                || e.PropertyName == nameof(InferenceRunnerViewModel.ConfidenceThreshold)
                || e.PropertyName == nameof(InferenceRunnerViewModel.MaxDetections)
                || e.PropertyName == nameof(InferenceRunnerViewModel.SelectedRightTabIndex))
            {
                OnPropertyChanged(e.PropertyName);
                NotifyCanRun();
                NotifyStatusBar();
            }
        };

        Report.PropertyChanged += (_, e) =>
        {
            OnPropertyChanged(e.PropertyName);
            NotifyStatusBar();
        };

    }

    /// <summary>
    /// Re-attaches the singleton subscriptions this page needs. Paired with <see cref="Deactivate"/>;
    /// NavigationView may unload and re-show the same page instance, so subscribing in the constructor
    /// only would leave a re-activated page dead.
    /// </summary>
    public void Activate()
    {
        if (_disposed || _isActive)
        {
            return;
        }

        _isActive = true;
        _workspaceSession.Changed += OnWorkspaceChanged;
    }

    /// <summary>
    /// Releases singleton subscriptions and stops in-flight work without disposing the view model.
    /// Safe to call on every unload; <see cref="Activate"/> restores the page on re-entry.
    /// </summary>
    public void Deactivate()
    {
        if (!_isActive)
        {
            return;
        }

        _isActive = false;
        _workspaceSession.Changed -= OnWorkspaceChanged;
        _loadCts?.Cancel();
        Runner.CancelRuns();
        LoadCommand.Cancel();
    }

    /// <summary>
    /// <see cref="Deactivate"/> plus a bounded wait for the model warm-up that was in flight. A native
    /// load cannot be interrupted, so this is how a page lets go of its view model without leaving the
    /// load running unsupervised. Returns false when the load outlived <paramref name="timeout"/>.
    /// </summary>
    public async Task<bool> DeactivateAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        Deactivate();
        return await Runner.WaitForWarmupIdleAsync(timeout, cancellationToken).ConfigureAwait(false);
    }

    public InferenceWorklistViewModel Worklist { get; }

    public InferenceCanvasViewModel Canvas { get; }

    public InferenceRunnerViewModel Runner { get; }

    public InferenceReportViewModel Report { get; }

    // Forwarding properties for flat UI binding compatibility
    public ObservableCollection<InferenceImageItem> Images => Worklist.Images;

    public ObservableCollection<InferenceGalleryOption> Galleries => Worklist.Galleries;

    public ObservableCollection<InferencePageModelOption> Models => Runner.Models;

    public ObservableCollection<InferenceDeviceOption> DeviceOptions => Runner.DeviceOptions;

    public ObservableCollection<InferenceDetectionRow> ResultRows => Canvas.ResultRows;

    public bool HasProjectContext
    {
        get => Worklist.HasProjectContext;
        set => Worklist.HasProjectContext = value;
    }

    public string ProjectTitle
    {
        get => Worklist.ProjectTitle;
        set => Worklist.ProjectTitle = value;
    }

    public string StatusText
    {
        get => Worklist.StatusText;
        set => Worklist.StatusText = value;
    }

    public bool IsLoading
    {
        get => Worklist.IsLoading;
        set => Worklist.IsLoading = value;
    }

    public bool IsRunning
    {
        get => Runner.IsRunning;
        set => Runner.IsRunning = value;
    }

    public bool IsWarmingUp => Runner.IsWarmingUp;

    public string WarmupStatusText => Runner.WarmupStatusText;

    public InferencePageModelOption? SelectedModel
    {
        get => Runner.SelectedModel;
        set => Runner.SelectedModel = value;
    }

    public InferenceDeviceOption? SelectedDevice
    {
        get => Runner.SelectedDevice;
        set => Runner.SelectedDevice = value;
    }

    public double ConfidenceThreshold
    {
        get => Runner.ConfidenceThreshold;
        set => Runner.ConfidenceThreshold = value;
    }

    public bool ShowsConfidenceThreshold => Runner.ShowsConfidenceThreshold;

    public int MaxDetections
    {
        get => Runner.MaxDetections;
        set => Runner.MaxDetections = value;
    }

    public bool ShowsMaxDetections => Runner.ShowsMaxDetections;

    public bool ShowsInferenceDeviceSelector => Runner.ShowsInferenceDeviceSelector;

    public int SelectedRightTabIndex
    {
        get => Runner.SelectedRightTabIndex;
        set => Runner.SelectedRightTabIndex = value;
    }

    public InferenceImageItem? SelectedImage
    {
        get => Worklist.SelectedImage;
        set => Worklist.SelectedImage = value;
    }

    public InferenceImageSourceMode SourceMode
    {
        get => Worklist.SourceMode;
        set => Worklist.SourceMode = value;
    }

    public InferenceGalleryOption? SelectedGallery
    {
        get => Worklist.SelectedGallery;
        set => Worklist.SelectedGallery = value;
    }

    public ImageSource? PreviewImage
    {
        get => Canvas.PreviewImage;
        set => Canvas.PreviewImage = value;
    }

    public InferenceDetectionRow? SelectedResultRow
    {
        get => Canvas.SelectedResultRow;
        set => Canvas.SelectedResultRow = value;
    }

    public string ActiveFileName
    {
        get => Canvas.ActiveFileName;
        set => Canvas.ActiveFileName = value;
    }

    public string ImageSizeText
    {
        get => Canvas.ImageSizeText;
        set => Canvas.ImageSizeText = value;
    }

    public string ResultHeadline
    {
        get => Report.ResultHeadline;
        set => Report.ResultHeadline = value;
    }

    public string ResultDetail
    {
        get => Report.ResultDetail;
        set => Report.ResultDetail = value;
    }

    public string ResultPerformance
    {
        get => Report.ResultPerformance;
        set => Report.ResultPerformance = value;
    }

    public bool HasResult
    {
        get => Report.HasResult;
        set => Report.HasResult = value;
    }

    public bool LastResultFailed
    {
        get => Report.LastResultFailed;
        set => Report.LastResultFailed = value;
    }

    public bool HasEvaluationPairs => Report.HasEvaluationPairs;

    public string EvaluationSummaryText => Report.EvaluationSummaryText;

    public int ImageCount => Worklist.ImageCount;

    public string ImageSourceSummary => Worklist.ImageSourceSummary;

    public bool IsExternalSource => Worklist.IsExternalSource;

    public bool IsGallerySource => Worklist.IsGallerySource;

    public bool HasPreviewImage => Canvas.HasPreviewImage;

    public bool ShowResultRowList => Canvas.ShowResultRowList;

    public bool CanRun =>
        HasProjectContext
        && !IsRunning
        && SelectedModel is not null
        && SelectedImage is not null
        && File.Exists(SelectedImage.ImagePath);

    // Status bar text projections
    public string StatusBarState =>
        !HasProjectContext ? "未选项目"
        : IsLoading ? "加载中"
        : IsRunning ? "推理中"
        : IsWarmingUp ? "预热中"
        : LastResultFailed ? "失败"
        : HasResult ? "完成"
        : SelectedModel is null ? "无模型" : "就绪";

    public string StatusBarModelText => SelectedModel is null ? "模型 —" : SelectedModel.DisplayText;

    public string StatusBarDeviceText => SelectedDevice is null ? "设备 —" : SelectedDevice.DisplayName;

    public string StatusBarListText => ImageCount <= 0 ? "列表 0 张" : $"列表 {ImageCount} 张";

    public string StatusBarImageText =>
        string.IsNullOrWhiteSpace(ActiveFileName) ? "当前 —"
        : string.IsNullOrWhiteSpace(ImageSizeText) ? ActiveFileName
        : $"{ActiveFileName} · {ImageSizeText}";

    public string StatusBarPerfText =>
        HasResult && !string.IsNullOrWhiteSpace(ResultPerformance)
            ? ResultPerformance
            : "性能 — 运行推理后显示预检 / 加载 / 推理耗时";

    private void NotifyCanRun()
    {
        OnPropertyChanged(nameof(CanRun));
        RunSelectedCommand.NotifyCanExecuteChanged();
    }

    private void NotifyStatusBar()
    {
        OnPropertyChanged(nameof(StatusBarState));
        OnPropertyChanged(nameof(StatusBarModelText));
        OnPropertyChanged(nameof(StatusBarDeviceText));
        OnPropertyChanged(nameof(StatusBarListText));
        OnPropertyChanged(nameof(StatusBarImageText));
        OnPropertyChanged(nameof(StatusBarPerfText));
        OnPropertyChanged(nameof(EvaluationSummaryText));
        OnPropertyChanged(nameof(HasEvaluationPairs));
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => LoadCommand.Execute(null);

    private void OnSelectedImageChanged(InferenceImageItem? value)
    {
        // Decode full-resolution image on demand for the canvas; worklist items keep lightweight thumbnails.
        PreviewImage = value is not null && !string.IsNullOrWhiteSpace(value.ImagePath) && File.Exists(value.ImagePath)
            ? (_imageSources.FromFile(value.ImagePath) ?? value.Preview)
            : value?.Preview;
        ActiveFileName = value?.DisplayName ?? string.Empty;
        ImageSizeText = FormatImageSize(PreviewImage);

        // Facade computed flags must be raised explicitly for empty-state bindings.
        OnPropertyChanged(nameof(HasPreviewImage));
        OnPropertyChanged(nameof(PreviewImage));

        if (value?.LastResultSummary is { } cached)
        {
            ApplyResult(cached, raw: null, persistToSelectedImage: false);
        }
        else
        {
            ClearResult();
        }

        NotifyCanRun();
        NotifyStatusBar();
        OnPropertyChanged(nameof(CanAdoptPredictedBoxes));
        AdoptPredictedBoxesCommand.NotifyCanExecuteChanged();
    }

    /// <summary>True when the current image comes from a gallery and the result carries boxes.</summary>
    public bool CanAdoptPredictedBoxes =>
        SelectedImage?.GalleryId is not null && Canvas.HasBoxOverlays;

    /// <summary>
    /// Appends the predicted boxes to the sample's annotations (pre-labeling). Existing boxes and
    /// overlapping same-class predictions are preserved/skipped, so this is safe to run repeatedly.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAdoptPredictedBoxes))]
    private async Task AdoptPredictedBoxesAsync()
    {
        var image = SelectedImage;
        if (image?.GalleryId is not Guid galleryId)
        {
            return;
        }

        if (_workspaceSession.Current.Project?.Id is not Guid projectId)
        {
            return;
        }

        try
        {
            var labels = await _projectLabels.ListAsync(projectId);
            var existing = await _annotations.LoadAsync(galleryId, image.DisplayName);
            var width = existing.ImageWidth > 0 ? existing.ImageWidth : Canvas.ImageWidth;
            var height = existing.ImageHeight > 0 ? existing.ImageHeight : Canvas.ImageHeight;
            var boxes = Canvas.ResultRows
                .Where(row => row.HasBox)
                .Select(row => new DetectionBoxResult
                {
                    Label = row.ClassName,
                    ClassName = row.ClassName,
                    Confidence = row.Confidence,
                    X = row.X,
                    Y = row.Y,
                    Width = row.Width,
                    Height = row.Height
                })
                .ToArray();

            var merged = DetectionAnnotationMerger.Merge(
                existing,
                boxes,
                labels.Select(label => label.Name).ToArray(),
                append: true,
                width,
                height);
            if (merged.IsFailure || merged.Value is null)
            {
                _snackbar.ShowWarning(merged.Error ?? "没有可采纳的预测框。", "推理");
                return;
            }

            var document = merged.Value;
            await _annotations.SaveAsync(
                galleryId,
                image.DisplayName,
                document.ImageWidth,
                document.ImageHeight,
                document.ImageClass,
                document.Boxes,
                document.IsReviewed);
            _snackbar.ShowSuccess($"已采纳 {document.Boxes.Count} 个预测框为标注。", "推理");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to adopt predicted boxes for {FileName}", image.DisplayName);
            _snackbar.ShowError($"采纳预测框失败：{ex.Message}", "推理");
        }
    }

    private static string FormatImageSize(ImageSource? source)
    {
        if (source is null) return string.Empty;
        return $"{source.Width} × {source.Height}";
    }

    private void ClearResult()
    {
        Canvas.ClearOverlays();
        ResultHeadline = string.Empty;
        ResultDetail = string.Empty;
        ResultPerformance = string.Empty;
        HasResult = false;
        LastResultFailed = false;
    }

    private void ApplyResult(InferenceResultSummary summary, object? raw, bool persistToSelectedImage = true)
    {
        Canvas.ApplyResult(summary, raw);
        ResultHeadline = summary.Headline;
        ResultDetail = summary.Detail;
        ResultPerformance = summary.PerformanceText;
        HasResult = true;
        LastResultFailed = !summary.Success;
        OnPropertyChanged(nameof(CanAdoptPredictedBoxes));
        AdoptPredictedBoxesCommand.NotifyCanExecuteChanged();

        if (persistToSelectedImage && SelectedImage is not null)
        {
            SelectedImage.LastResultSummary = summary;
            if (SelectedModel is not null)
            {
                if (raw is InferenceResult res)
                {
                    var (predicted, confidence) = InferenceOkNgPrediction.TryResolve(
                        res,
                        SelectedModel.ModelType);
                    SelectedImage.PredictedClass = predicted;
                    SelectedImage.PredictedConfidence = confidence;
                }
            }
        }

        Report.RefreshEvaluationSummary(Images);
    }

    public Task OnPageLoadedAsync() => LoadCommand.ExecuteAsync(null);

    public void Dispose()
    {
        if (_disposed) return;
        Deactivate();
        Runner.DetachWarmup();
        _disposed = true;
        _loadCts?.Dispose();
        // RunSelected/RunAll take no CancellationToken, so their generated commands cannot be
        // cancelled — Cancel() is a no-op. CancelRuns above is what actually stops a batch.
        RunSelectedCommand.Cancel();
        RunAllCommand.Cancel();
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _loadCts.Token;

        try
        {
            IsLoading = true;
            var session = _workspaceSession.Current;
            if (session.Project is null)
            {
                HasProjectContext = false;
                ProjectTitle = "推理";
                StatusText = "请先在「项目」中选择项目。";
                Models.Clear();
                Galleries.Clear();
                Images.Clear();
                ClearResult();
                NotifyCanRun();
                return;
            }

            HasProjectContext = true;
            ProjectTitle = $"推理 · {session.Project.Name}";
            StatusText = "加载模型与图库…";

            await LoadModelsAsync(session.Project.Id, session.Project.Type, ct).ConfigureAwait(true);
            await LoadGalleriesAsync(session.Project.Id, session.GalleryId, ct).ConfigureAwait(true);

            StatusText = Models.Count == 0
                ? "当前项目没有可推理的已训练模型。"
                : $"已加载 {Models.Count} 个模型。";
            NotifyCanRun();
            Runner.ScheduleWarmup();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inference page load failed");
            StatusText = ex.Message;
            _snackbar.ShowError(ex.Message, "推理");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadModelsAsync(Guid projectId, string projectType, CancellationToken ct)
    {
        Models.Clear();
        var result = await _mediator.Send(new GetModelsByProjectQuery(projectId), ct).ConfigureAwait(true);
        if (result.IsFailure || result.Value is null) return;

        var projectWorkflow = ProjectWorkflow.Resolve(projectType);
        var items = result.Value
            // Same eligibility rule as gallery batch and labeling auto-inference: a model that is
            // still creating or archived must not appear as a runnable option.
            .Where(m => TrainedModelStatuses.CanRunInference(m.Status))
            .Where(m => TaskCompatibilityCatalog.Validate(projectWorkflow, m.ModelType).IsSuccess)
            .OrderByDescending(m => m.CreatedAt)
            .ToList();

        foreach (var m in items)
        {
            Models.Add(new InferencePageModelOption(
                m.Id,
                m.Name,
                m.ModelType,
                m.CheckpointPath));
        }

        SelectedModel = Models.FirstOrDefault();
        Runner.RefreshDeviceOptionsForModel(SelectedModel);
    }

    private async Task LoadGalleriesAsync(Guid projectId, Guid? activeGalleryId, CancellationToken ct)
    {
        Galleries.Clear();
        var result = await _mediator.Send(new GetGalleriesByProjectQuery(projectId), ct).ConfigureAwait(true);
        if (result.IsFailure || result.Value is null) return;

        foreach (var g in result.Value)
        {
            Galleries.Add(new InferenceGalleryOption
            {
                Id = g.Id,
                Name = g.Name,
                SampleCount = g.SampleCount
            });
        }

        SelectedGallery = (activeGalleryId.HasValue
            ? Galleries.FirstOrDefault(g => g.Id == activeGalleryId.Value)
            : null) ?? Galleries.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunSelectedAsync()
    {
        if (SelectedModel is null || SelectedImage is null) return;

        var model = SelectedModel;
        var image = SelectedImage;
        var success = await Runner.RunOneAsync(
            model,
            image,
            (summary, raw) => ApplyResult(summary, raw, persistToSelectedImage: true),
            ClearResult,
            status => StatusText = status).ConfigureAwait(true);

        if (success)
        {
            SelectedRightTabIndex = 1;
        }
    }

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (!HasProjectContext || SelectedModel is null || Images.Count == 0 || IsRunning) return;

        var model = SelectedModel;
        var targets = Images.ToList();
        var total = targets.Count;
        var ok = 0;
        var failed = 0;
        var batchSw = Stopwatch.StartNew();

        var runDevice = Runner.ResolveRunDevice(model);
        var isCpuMode = runDevice.Contains("cpu", StringComparison.OrdinalIgnoreCase);
        var maxConcurrency = isCpuMode
            ? Math.Clamp(Environment.ProcessorCount / 2, 1, 4)
            : 1;

        // Throttle progress text so the status bar is not rewritten on every sample.
        long lastProgressTick = 0;
        const int progressThrottleMs = 150;

        // One shared CTS for the whole batch so Stop cancels all items without
        // each item recreating/cancelling the token (the previous 99% failure mode).
        var batchToken = Runner.BeginSharedRun();

        try
        {
            IsRunning = true;
            StatusText = $"正在准备流水线批量推理（共 {total} 张，并发数：{maxConcurrency}）...";

            await InferenceBatchPipeline.ExecuteAsync(
                targets,
                async (image, token) =>
                {
                    // Silent per-item path: persist results on the item only.
                    // Do NOT clear/rebuild canvas overlays or status-bar evaluation on every sample
                    // (that was the main cause of UI stutter during「推理全部」).
                    var success = await Runner.RunOneAsync(
                        model,
                        image,
                        applyResult: (summary, _) =>
                        {
                            image.LastResultSummary = summary;
                        },
                        clearResult: static () => { },
                        setStatusText: static _ => { },
                        ct: token,
                        manageIsRunning: false,
                        showErrorSnackbar: false).ConfigureAwait(false);

                    if (success) Interlocked.Increment(ref ok);
                    else Interlocked.Increment(ref failed);
                },
                maxConcurrency: maxConcurrency,
                onProgress: (processed, totalCount, item) =>
                {
                    var now = Environment.TickCount64;
                    if (processed < totalCount)
                    {
                        var prev = Interlocked.Read(ref lastProgressTick);
                        if (now - prev < progressThrottleMs)
                        {
                            return;
                        }

                        Interlocked.Exchange(ref lastProgressTick, now);
                    }

                    var name = item.DisplayName;
                    _uiDispatcher.Post(() =>
                        StatusText = $"正在流水线批量推理 ({processed}/{totalCount})：{name}");
                },
                cancellationToken: batchToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (batchToken.IsCancellationRequested)
        {
            // Stopped partway: report what actually ran and do not build a report that would look
            // like the whole batch was processed.
            batchSw.Stop();
            Report.RefreshEvaluationSummary(targets);
            NotifyStatusBar();

            var stoppedText =
                $"批量推理已停止：已处理 {ok + failed} / {total} 张（成功 {ok}，失败 {failed}）";
            StatusText = stoppedText;
            _snackbar.ShowWarning(stoppedText, "推理");
            _logger.LogInformation(
                "Batch inference cancelled after {Processed}/{Total} items (ok={Ok}, failed={Failed})",
                ok + failed,
                total,
                ok,
                failed);
            return;
        }
        finally
        {
            IsRunning = false;
            NotifyCanRun();
        }

        batchSw.Stop();

        // One-shot UI refresh after the whole batch finishes.
        Report.RefreshEvaluationSummary(targets);
        if (SelectedImage?.LastResultSummary is { } selectedSummary)
        {
            ApplyResult(selectedSummary, raw: null, persistToSelectedImage: false);
        }

        NotifyStatusBar();

        SelectedRightTabIndex = 1;
        StatusText = $"流水线批量推理完成：成功 {ok}，失败 {failed} · {EvaluationSummaryText}";
        _snackbar.ShowInfo(StatusText, "推理");

        var report = Report.BuildBatchReport(model, targets, ok, failed, batchSw.Elapsed.TotalMilliseconds, SelectedDevice?.DisplayName ?? Runner.ResolveRunDevice(model));
        await Report.ShowBatchReportAsync(report).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task StopAsync() => Runner.StopCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task ShowLastBatchReportAsync() => Report.ShowLastBatchReportCommand.ExecuteAsync(null);

    [RelayCommand]
    private Task SetExpectedOkAsync(InferenceImageItem? item)
    {
        Worklist.SetExpectedOk(item);
        Report.RefreshEvaluationSummary(Images);
        NotifyStatusBar();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task SetExpectedNgAsync(InferenceImageItem? item)
    {
        Worklist.SetExpectedNg(item);
        Report.RefreshEvaluationSummary(Images);
        NotifyStatusBar();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ClearExpectedAsync(InferenceImageItem? item)
    {
        Worklist.ClearExpected(item);
        Report.RefreshEvaluationSummary(Images);
        NotifyStatusBar();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ClearGalleryImagesAsync()
    {
        Worklist.ClearGalleryImages();
        Report.RefreshEvaluationSummary(Images);
        NotifyCanRun();
        NotifyStatusBar();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task ClearAllImagesAsync()
    {
        Worklist.ClearAllImages();
        Report.RefreshEvaluationSummary(Images);
        NotifyCanRun();
        NotifyStatusBar();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task ImportExternalImagesAsync()
    {
        if (!HasProjectContext)
        {
            return;
        }

        await Worklist.ImportExternalFilesAsync().ConfigureAwait(true);
        Report.RefreshEvaluationSummary(Images);
        NotifyCanRun();
        NotifyStatusBar();
        OnPropertyChanged(nameof(ImageSourceSummary));
    }

    [RelayCommand]
    private void ClearExternalImages()
    {
        Worklist.ClearExternalImages();
        Report.RefreshEvaluationSummary(Images);
        NotifyCanRun();
        NotifyStatusBar();
        OnPropertyChanged(nameof(ImageSourceSummary));
    }

    [RelayCommand]
    private async Task LoadGallerySamplesAsync()
    {
        if (!HasProjectContext || SelectedGallery is null)
        {
            return;
        }

        await Worklist.AppendSelectedGalleryAsync().ConfigureAwait(true);
        Report.RefreshEvaluationSummary(Images);
        NotifyCanRun();
        NotifyStatusBar();
        OnPropertyChanged(nameof(ImageSourceSummary));
    }

    [RelayCommand]
    private async Task ShowImageSourceDialogAsync()
    {
        if (!HasProjectContext)
        {
            _snackbar.ShowInfo("请先在「项目」中选择项目。", "推理");
            return;
        }

        await _appDialogService.ShowAsync(AppDialogKind.InferenceImageSource, this).ConfigureAwait(true);
        OnPropertyChanged(nameof(ImageSourceSummary));
        NotifyCanRun();
        NotifyStatusBar();
    }
}
