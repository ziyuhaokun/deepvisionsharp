using DeepVisionSharp.Application.Common;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Navigation;

namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public sealed partial class GalleryListViewModel : ObservableObject
{
    private const int KeywordDebounceMilliseconds = 250;

    private readonly IMediator _mediator;
    private readonly IGalleryTransferService _transferService;
    private readonly IProjectFileStore _fileStore;
    private readonly ISampleThumbnailService _sampleThumbnails;
    private readonly IImageSourceFactory _imageSources;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly IAppNavigationService _navigation;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly IProcessLauncher _processLauncher;
    private readonly ILogger<GalleryListViewModel> _logger;
    private readonly Func<bool> _isBusy;

    private CancellationTokenSource? _keywordFilterCts;

    public GalleryListViewModel(
        IMediator mediator,
        IGalleryTransferService transferService,
        IProjectFileStore fileStore,
        ISampleThumbnailService sampleThumbnails,
        IImageSourceFactory imageSources,
        WorkspaceSessionService workspaceSession,
        IAppNavigationService navigation,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        IProcessLauncher processLauncher,
        ILogger<GalleryListViewModel> logger,
        Func<bool> isBusy)
    {
        _mediator = mediator;
        _transferService = transferService;
        _fileStore = fileStore;
        _sampleThumbnails = sampleThumbnails;
        _imageSources = imageSources;
        _workspaceSession = workspaceSession;
        _navigation = navigation;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _processLauncher = processLauncher;
        _logger = logger;
        _isBusy = isBusy;
    }

    public ProjectLabelStatisticsDialogState LabelStatisticsDialog { get; } = new();

    /// <summary>
    /// 稳定集合：刷新与过滤时采用 Reconcile 增量调整，保持同一实例引用，避免卡片容器频繁销毁与闪烁。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<GalleryRow> _galleries = new();

    [ObservableProperty]
    private ObservableCollection<GalleryRow> _filteredGalleries = new();

    public Guid? CurrentProjectId { get; private set; }

    public string CurrentProjectType { get; private set; } = string.Empty;

    public bool ShowsGalleryBatchInference => true;

    /// <summary>Label statistics for the image-class labels of a gallery.</summary>
    public bool ShowsGalleryLabelStatistics => true;

    public bool ShowsGallerySplit => true;

    public bool ShowsGalleryEdit => true;

    [ObservableProperty]
    private string _keyword = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _projectName = "图库";

    [ObservableProperty]
    private string _projectDescription = "从项目面板选择项目后，在这里组织采集批次、样本导入和标注进度。";

    [ObservableProperty]
    private int _galleryCount;

    [ObservableProperty]
    private int _sampleCount;

    [ObservableProperty]
    private int _annotatedCount;

    [ObservableProperty]
    private double _projectProgress;

    [ObservableProperty]
    private int _filteredGalleryCount;

    [ObservableProperty]
    private bool _hasGalleries;

    [ObservableProperty]
    private bool _hasFilteredGalleries;

    [ObservableProperty]
    private bool _isGallerySelectionMode;

    public string ProjectProgressText => ProjectProgress.ToString("P0");

    public string EmptyStateTitle => HasGalleries ? "未找到匹配图库" : "暂无图库";

    public string EmptyStateDescription => HasGalleries ? "尝试调整搜索关键词" : "创建第一个图库以开始组织样本";

    public bool HasEmptyState => !HasFilteredGalleries;

    public int SelectedGalleryCount => Galleries.Count(gallery => gallery.IsSelected);

    public bool CanMergeGalleries => SelectedGalleryCount >= 2 && CanRunGalleryOperation();

    /// <summary>导入/导出进行中时置真，禁止删除/进入/编辑图库，避免与 Transfer 并发操作数据。</summary>
    public bool IsBlockedByTransfer { get; private set; }

    public event Action? SelectionStateChanged;

    public event Action? BusyStateAffectingCommandsChanged;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        _logger.LogInformation("Loading gallery page data");

        try
        {
            var session = _workspaceSession.Current;
            if (session.Project is null)
            {
                ApplyGallerySnapshot(Array.Empty<ProjectGallery>());
                CurrentProjectId = null;
                CurrentProjectType = string.Empty;
                NotifyProjectTypeUi();
                ProjectName = "图库";
                ProjectDescription = "请先从项目面板进入一个项目。";
                _logger.LogInformation("Gallery page loaded without a selected project");
                return;
            }

            var projectResult = await _mediator.Send(
                new GetProjectByIdQuery(session.Project.Id),
                cancellationToken);
            if (!_workspaceSession.IsCurrent(session.Version))
            {
                return;
            }

            if (projectResult.IsFailure)
            {
                _snackbar.ShowError(projectResult.Error ?? "加载项目失败。", "图库");
                return;
            }

            var project = projectResult.Value;
            if (project is null)
            {
                ApplyGallerySnapshot(Array.Empty<ProjectGallery>());
                _workspaceSession.ClearProjectIfCurrent(session.Project.Id);
                CurrentProjectId = null;
                CurrentProjectType = string.Empty;
                NotifyProjectTypeUi();
                ProjectName = "图库";
                ProjectDescription = "当前项目不存在或已被删除。";
                return;
            }

            if (CurrentProjectId != project.Id)
            {
                ApplyGallerySnapshot(Array.Empty<ProjectGallery>());
            }

            CurrentProjectId = project.Id;
            CurrentProjectType = project.Type;
            NotifyProjectTypeUi();
            ProjectName = project.Name;
            ProjectDescription = "管理该项目下的图库、样本导入、导出与标注进度。";

            var galleriesResult = await _mediator.Send(
                new GetGalleriesByProjectQuery(project.Id),
                cancellationToken);
            if (!_workspaceSession.IsCurrent(session.Version))
            {
                return;
            }

            if (galleriesResult.IsFailure || galleriesResult.Value is null)
            {
                _snackbar.ShowError(galleriesResult.Error ?? "加载图库列表失败。", "图库");
                return;
            }

            ApplyGallerySnapshot(galleriesResult.Value);
            _ = LoadGalleryThumbnailsAsync(Galleries.ToArray(), cancellationToken);

            _logger.LogInformation("Loaded {GalleryCount} galleries for project {ProjectId}", Galleries.Count, project.Id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Gallery page load was canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load gallery page data");
            _snackbar.ShowError("加载图库失败，详细信息已写入日志。", "图库");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ShowProjectLabelStatisticsDialogAsync()
    {
        if (CurrentProjectId is null)
        {
            _snackbar.ShowWarning("请先选择项目。", "标签统计");
            return;
        }

        var result = await _mediator.Send(new GetProjectLabelStatisticsQuery(CurrentProjectId.Value));
        if (result.IsFailure || result.Value is null)
        {
            _snackbar.ShowError(result.Error ?? "加载标签统计失败。", "标签统计");
            return;
        }

        LabelStatisticsDialog.Reset(ProjectName, result.Value);
        await _appDialogService.ShowAsync(
            AppDialogKind.ProjectLabelStatistics,
            LabelStatisticsDialog,
            new DialogOptions(Title: "标签统计", PrimaryButtonText: "关闭", CloseButtonText: string.Empty));
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ShowGalleryLabelStatisticsDialogAsync(Guid galleryId)
    {
        if (galleryId == Guid.Empty)
        {
            return;
        }

        var gallery = Galleries.FirstOrDefault(item => item.Id == galleryId);
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到图库。", "标签统计");
            return;
        }

        var result = await _mediator.Send(new GetGalleryLabelStatisticsQuery(galleryId));
        if (result.IsFailure || result.Value is null)
        {
            _snackbar.ShowError(result.Error ?? "加载图库标签统计失败。", "标签统计");
            return;
        }

        LabelStatisticsDialog.ResetForGallery(gallery.Name, result.Value);
        await _appDialogService.ShowAsync(
            AppDialogKind.ProjectLabelStatistics,
            LabelStatisticsDialog,
            new DialogOptions(
                Title: "图库标签统计",
                PrimaryButtonText: "关闭",
                CloseButtonText: string.Empty));
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private void OpenGalleryFolder(Guid galleryId)
    {
        var gallery = Galleries.FirstOrDefault(item => item.Id == galleryId);
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到图库。", "图库");
            return;
        }

        var directory = _transferService.GetGalleryDirectory(gallery.ProjectId, gallery.Id, ensureExists: true);
        if (!_processLauncher.TryOpenFolder(directory))
        {
            _snackbar.ShowWarning("无法打开图库目录。", "图库");
            return;
        }

        _snackbar.ShowSuccess($"已打开「{gallery.Name}」目录。", "图库");
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    public void EnterGallery(Guid galleryId)
    {
        var gallery = Galleries.FirstOrDefault(item => item.Id == galleryId);
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到图库。", "图库");
            return;
        }

        if (CurrentProjectId is null
            || !_workspaceSession.TryEnterGallery(galleryId, CurrentProjectId.Value))
        {
            _snackbar.ShowWarning("当前图库不属于活动项目，请重新进入项目。", "图库");
            return;
        }

        _navigation.Navigate(AppRoute.Labeling);
        _logger.LogInformation("Entering labeling page for gallery {GalleryId}", galleryId);
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private void ToggleGallerySelectionMode()
    {
        IsGallerySelectionMode = !IsGallerySelectionMode;
        if (!IsGallerySelectionMode)
        {
            foreach (var gallery in Galleries)
            {
                gallery.IsSelected = false;
            }
        }

        NotifyGallerySelectionStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private void SelectAllGalleries()
    {
        foreach (var gallery in FilteredGalleries)
        {
            gallery.IsSelected = true;
        }
        NotifyGallerySelectionStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private void DeselectAllGalleries()
    {
        foreach (var gallery in Galleries)
        {
            gallery.IsSelected = false;
        }
        NotifyGallerySelectionStateChanged();
    }

    public void SetBlockedByTransfer(bool blocked)
    {
        if (IsBlockedByTransfer == blocked)
        {
            return;
        }

        IsBlockedByTransfer = blocked;
        NotifyBusyStateChanged();
    }

    public void NotifyBusyStateChanged()
    {
        OpenGalleryFolderCommand.NotifyCanExecuteChanged();
        EnterGalleryCommand.NotifyCanExecuteChanged();
        ToggleGallerySelectionModeCommand.NotifyCanExecuteChanged();
        SelectAllGalleriesCommand.NotifyCanExecuteChanged();
        DeselectAllGalleriesCommand.NotifyCanExecuteChanged();
        ShowGalleryLabelStatisticsDialogCommand.NotifyCanExecuteChanged();
        NotifyGallerySelectionStateChanged();
        BusyStateAffectingCommandsChanged?.Invoke();
    }

    public async Task RecalculateProjectProgressAsync()
    {
        if (CurrentProjectId is null)
        {
            return;
        }

        var projectId = CurrentProjectId.Value;
        var sessionVersion = _workspaceSession.Current.Version;

        await _mediator.Send(new RecalculateProjectProgressCommand(projectId));
        if (!IsRefreshStillCurrent(projectId, sessionVersion))
        {
            return;
        }

        var projectResult = await _mediator.Send(new GetProjectByIdQuery(projectId));
        if (!IsRefreshStillCurrent(projectId, sessionVersion))
        {
            return;
        }

        if (projectResult.IsSuccess && projectResult.Value is not null)
        {
            ProjectProgress = projectResult.Value.Progress;
            SampleCount = projectResult.Value.SampleCount;
        }
    }

    public async Task RefreshCurrentProjectGalleriesAsync()
    {
        if (CurrentProjectId is null)
        {
            return;
        }

        var projectId = CurrentProjectId.Value;
        var sessionVersion = _workspaceSession.Current.Version;

        var galleriesResult = await _mediator.Send(new GetGalleriesByProjectQuery(projectId));
        if (!IsRefreshStillCurrent(projectId, sessionVersion))
        {
            return;
        }

        if (galleriesResult.IsFailure || galleriesResult.Value is null)
        {
            _snackbar.ShowError(galleriesResult.Error ?? "刷新图库列表失败。", "图库");
            return;
        }

        ApplyGallerySnapshot(galleriesResult.Value);
        _ = LoadGalleryThumbnailsAsync(Galleries.ToArray(), CancellationToken.None);
    }

    private bool IsRefreshStillCurrent(Guid projectId, long sessionVersion)
    {
        var session = _workspaceSession.Current;
        return session.Project?.Id == projectId
            && session.Version == sessionVersion
            && CurrentProjectId == projectId;
    }

    public void InsertGallery(GalleryRow row, int index = 0)
    {
        AttachGalleryRow(row);
        var insertAt = index <= 0 ? 0 : Math.Min(index, Galleries.Count);
        Galleries.Insert(insertAt, row);
        RefreshMetrics();
        RefreshFilteredGalleries();
        NotifyGallerySelectionStateChanged();
    }

    public void ReplaceGallery(GalleryRow row)
    {
        var existing = Galleries.FirstOrDefault(gallery => gallery.Id == row.Id);
        if (existing is not null)
        {
            var index = Galleries.IndexOf(existing);
            existing.SelectionChanged -= OnGalleryRowSelectionChanged;
            Galleries[index] = row;
            AttachGalleryRow(row);
            RefreshMetrics();
            RefreshFilteredGalleries();
            NotifyGallerySelectionStateChanged();
        }
    }

    public void RemoveGallery(Guid galleryId)
    {
        var existing = Galleries.FirstOrDefault(gallery => gallery.Id == galleryId);
        if (existing is not null)
        {
            existing.SelectionChanged -= OnGalleryRowSelectionChanged;
            Galleries.Remove(existing);
            RefreshMetrics();
            RefreshFilteredGalleries();
            NotifyGallerySelectionStateChanged();
        }
    }

    public void ClearSelectionMode()
    {
        IsGallerySelectionMode = false;
        foreach (var gallery in Galleries)
        {
            gallery.IsSelected = false;
        }

        NotifyGallerySelectionStateChanged();
    }

    public bool CanRunGalleryOperation() => !_isBusy() && !IsBlockedByTransfer;

    private void NotifyProjectTypeUi()
    {
        OnPropertyChanged(nameof(CurrentProjectType));
        OnPropertyChanged(nameof(ShowsGalleryBatchInference));
        OnPropertyChanged(nameof(ShowsGalleryLabelStatistics));
        OnPropertyChanged(nameof(ShowsGallerySplit));
        OnPropertyChanged(nameof(ShowsGalleryEdit));
        BusyStateAffectingCommandsChanged?.Invoke();
    }

    private void NotifyGallerySelectionStateChanged()
    {
        OnPropertyChanged(nameof(SelectedGalleryCount));
        OnPropertyChanged(nameof(CanMergeGalleries));
        SelectionStateChanged?.Invoke();
    }

    private void AttachGalleryRow(GalleryRow row)
    {
        row.SelectionChanged += OnGalleryRowSelectionChanged;
    }

    private void OnGalleryRowSelectionChanged(GalleryRow row)
    {
        NotifyGallerySelectionStateChanged();
    }

    partial void OnKeywordChanged(string value)
    {
        _keywordFilterCts?.Cancel();
        _keywordFilterCts?.Dispose();
        _keywordFilterCts = new CancellationTokenSource();
        _ = DebounceFilteredRefreshAsync(_keywordFilterCts);
    }

    private async Task DebounceFilteredRefreshAsync(CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            await Task.Delay(KeywordDebounceMilliseconds, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_keywordFilterCts, cts))
            {
                _keywordFilterCts.Dispose();
                _keywordFilterCts = null;
            }
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        RefreshFilteredGalleries();
    }

    partial void OnProjectProgressChanged(double value) => OnPropertyChanged(nameof(ProjectProgressText));

    partial void OnHasGalleriesChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }

    partial void OnHasFilteredGalleriesChanged(bool value) => OnPropertyChanged(nameof(HasEmptyState));

    private void ApplyGallerySnapshot(IReadOnlyList<ProjectGallery> rawGalleries)
    {
        var existingMap = Galleries.ToDictionary(gallery => gallery.Id);
        var desiredRows = new List<GalleryRow>(rawGalleries.Count);

        foreach (var gallery in rawGalleries)
        {
            if (existingMap.TryGetValue(gallery.Id, out var existing))
            {
                existing.ApplyFromGallery(gallery);
                desiredRows.Add(existing);
            }
            else
            {
                var newRow = GalleryRow.CreateFromGallery(gallery);
                AttachGalleryRow(newRow);
                desiredRows.Add(newRow);
            }
        }

        // 清理已删除的图库事件监听
        var desiredIdSet = new HashSet<Guid>(desiredRows.Select(row => row.Id));
        foreach (var row in Galleries)
        {
            if (!desiredIdSet.Contains(row.Id))
            {
                row.SelectionChanged -= OnGalleryRowSelectionChanged;
            }
        }

        CollectionReconciler.Reconcile(Galleries, desiredRows);
        RefreshMetrics();
        RefreshFilteredGalleries();
        NotifyGallerySelectionStateChanged();
    }

    private void RefreshMetrics()
    {
        GalleryCount = Galleries.Count;
        SampleCount = Galleries.Sum(gallery => gallery.SampleCount);
        AnnotatedCount = Galleries.Sum(gallery => gallery.AnnotatedCount);
        ProjectProgress = SampleCount > 0 ? (double)AnnotatedCount / SampleCount : 0;
        HasGalleries = GalleryCount > 0;
    }

    private void RefreshFilteredGalleries()
    {
        string keyword = Keyword.Trim();

        var desired = Galleries
            .Where(gallery => string.IsNullOrWhiteSpace(keyword)
                || gallery.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || gallery.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(gallery => gallery.LastUpdatedAt)
            .ToArray();

        if (FilteredGalleries.Count == 0 && desired.Length > 0)
        {
            FilteredGalleries = new ObservableCollection<GalleryRow>(desired);
        }
        else
        {
            CollectionReconciler.Reconcile(FilteredGalleries, desired);
        }

        FilteredGalleryCount = FilteredGalleries.Count;
        HasFilteredGalleries = FilteredGalleryCount > 0;
    }

    /// <summary>
    /// 将 target 增量调整为 desired 的顺序与内容：同实例（引用相等）原地保留，
    /// 仅对新增/移除/位移做最小集合操作，避免 ItemsControl 重建既有容器。
    /// </summary>
    private static void ReconcileCollection<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var i = 0; i < desired.Count; i++)
        {
            var desiredItem = desired[i];
            if (i < target.Count && ReferenceEquals(target[i], desiredItem))
            {
                continue;
            }

            var moveIndex = -1;
            for (var j = i + 1; j < target.Count; j++)
            {
                if (ReferenceEquals(target[j], desiredItem))
                {
                    moveIndex = j;
                    break;
                }
            }

            if (moveIndex >= 0)
            {
                target.Move(moveIndex, i);
            }
            else if (i < target.Count)
            {
                target[i] = desiredItem;
            }
            else
            {
                target.Add(desiredItem);
            }
        }
    }

    private async Task LoadGalleryThumbnailsAsync(
        IReadOnlyList<GalleryRow> rows,
        CancellationToken cancellationToken)
    {
        var pending = rows
            .Where(row => row.Thumbnail is null && row.SampleCount > 0)
            .ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        using var concurrencyGate = new SemaphoreSlim(4);
        var tasks = pending.Select(async row =>
        {
            var entered = false;
            try
            {
                // WaitAsync must be inside the try: when it throws on cancellation the task ends
                // Faulted rather than Canceled, Task.WhenAll then faults, and the fire-and-forget
                // caller never observes it — the process-wide handler reports an unhandled
                // exception dialog for a routine navigation.
                await concurrencyGate.WaitAsync(cancellationToken);
                entered = true;

                if (cancellationToken.IsCancellationRequested || row.Thumbnail is not null)
                {
                    return;
                }

                var imageDirectory = _fileStore.GetGalleryImagesDirectory(row.ProjectId, row.Id, ensureExists: false);
                if (!Directory.Exists(imageDirectory))
                {
                    return;
                }

                var firstImage = Directory.EnumerateFiles(imageDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(file =>
                    {
                        var ext = Path.GetExtension(file);
                        return ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                            || ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
                    });

                if (firstImage is null || !File.Exists(firstImage))
                {
                    return;
                }

                row.ThumbnailPath = firstImage;
                var bytes = await _sampleThumbnails.GetThumbnailAsync(firstImage, 480, cancellationToken);
                if (bytes is { Length: > 0 } && !cancellationToken.IsCancellationRequested)
                {
                    var source = _imageSources.FromBytes(bytes);
                    if (source is not null)
                    {
                        row.Thumbnail = source;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load thumbnail for gallery {GalleryId}", row.Id);
            }
            finally
            {
                // Only release when this task actually took the slot, otherwise the gate is
                // over-released and later batches stop being throttled.
                if (entered)
                {
                    concurrencyGate.Release();
                }
            }
        });

        await Task.WhenAll(tasks);
    }
}
