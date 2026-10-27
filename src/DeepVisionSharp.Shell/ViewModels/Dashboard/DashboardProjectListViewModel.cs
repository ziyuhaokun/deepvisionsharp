using DeepVisionSharp.Application.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Navigation;

namespace DeepVisionSharp.Shell.ViewModels.Dashboard;

public sealed partial class DashboardProjectListViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly IAppNavigationService _navigation;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly ISampleThumbnailService _sampleThumbnails;
    private readonly IImageSourceFactory _imageSources;
    private readonly ILogger<DashboardProjectListViewModel> _logger;

    public DashboardProjectListViewModel(
        IMediator mediator,
        WorkspaceSessionService workspaceSession,
        IAppNavigationService navigation,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ISampleThumbnailService sampleThumbnails,
        IImageSourceFactory imageSources,
        ILogger<DashboardProjectListViewModel> logger)
    {
        _mediator = mediator;
        _workspaceSession = workspaceSession;
        _navigation = navigation;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _sampleThumbnails = sampleThumbnails;
        _imageSources = imageSources;
        _logger = logger;

        ProjectTypeOptions.Add(new ProjectTypeFilterOption(string.Empty, "全部类型"));
        ProjectTypeOptions.Add(CreateProjectTypeOption(ProjectTypes.Classification));
    }

    private const int KeywordDebounceMilliseconds = 250;

    // 稳定集合：刷新/筛选原地增量更新（Add/Remove/Move/Replace），
    // 保持同一实例引用，避免整表替换导致 ItemsControl 重建容器。
    [ObservableProperty]
    private ObservableCollection<ProjectRow> _projects = new();

    [ObservableProperty]
    private ObservableCollection<ProjectRow> _filteredProjects = new();

    private CancellationTokenSource? _keywordFilterCts;

    public ObservableCollection<ProjectTypeFilterOption> ProjectTypeOptions { get; } = new();

    private static ProjectTypeFilterOption CreateProjectTypeOption(string type)
        => new(type, ProjectTypes.GetDisplayLabel(type));

    [ObservableProperty]
    private string _keyword = string.Empty;

    [ObservableProperty]
    private string _selectedType = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isProjectOperationInProgress;

    [ObservableProperty]
    private int _projectCount;

    [ObservableProperty]
    private int _totalSamples;

    [ObservableProperty]
    private int _totalLabels;

    [ObservableProperty]
    private int _filteredProjectCount;

    [ObservableProperty]
    private bool _hasProjects;

    [ObservableProperty]
    private bool _hasFilteredProjects;

    public string EmptyStateTitle => HasProjects ? "未找到匹配项目" : "暂无项目";

    public string EmptyStateDescription => HasProjects ? "尝试调整搜索关键词或筛选条件" : "创建您的第一个项目，开始管理深度学习数据集";

    public bool HasEmptyState => !HasFilteredProjects;

    /// <summary>导入/导出进行中时置真，禁止删除/进入项目，避免与 Transfer 并发操作数据。</summary>
    public bool IsBlockedByTransfer { get; private set; }

    public bool CanStartProjectOperation => !IsLoading && !IsProjectOperationInProgress && !IsBlockedByTransfer;

    public string ResultSummary => $"{FilteredProjectCount} / {ProjectCount} 个项目";

    public event Action? ProjectSnapshotChanged;

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            await RefreshProjectsAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _logger.LogInformation("Dashboard loaded {ProjectCount} projects", Projects.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Dashboard load was canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard projects");
            _snackbar.ShowError("加载项目列表失败，详细信息已写入日志。", "项目");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartProjectOperation))]
    private async Task ConfirmDeleteProjectAsync(Guid projectId)
    {
        var row = Projects.FirstOrDefault(project => project.Id == projectId);
        if (row is null)
        {
            _snackbar.ShowWarning("未找到项目。", "项目");
            return;
        }

        // 文案需覆盖实际删除范围（SqliteProjectRepository.DeleteAsync 还会删模型库、训练任务与磁盘目录）。
        var request = new ConfirmDialogRequest(
            $"即将删除项目「{row.Name}」（{row.SampleCountText}）及其全部数据，此操作不可恢复：\n"
            + "· 图库、样本与标注、项目标签\n"
            + "· 模型库与训练任务\n"
            + "· 磁盘项目目录（含未入库的 runs / models 产物）");
        var result = await _appDialogService.ShowAsync(
            AppDialogKind.Confirm,
            request,
            new DialogOptions(
                Title: "确认删除项目",
                PrimaryButtonText: "删除",
                DefaultButton: AppDialogDefaultButton.Close));

        if (result == AppDialogResult.Primary)
        {
            await DeleteProjectCommand.ExecuteAsync(projectId);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartProjectOperation))]
    private async Task DeleteProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var row = Projects.FirstOrDefault(project => project.Id == projectId);
        IsProjectOperationInProgress = true;
        try
        {
            var result = await _mediator.Send(new DeleteProjectCommand(projectId), cancellationToken);
            if (result.IsSuccess)
            {
                _workspaceSession.ClearProjectIfCurrent(projectId);
                RemoveProject(projectId);
                _snackbar.ShowSuccess($"已删除项目「{row?.Name ?? projectId.ToString()}」。", "项目");
            }
            else
            {
                _snackbar.ShowError(result.Error ?? "未找到要删除的项目。", "项目");
            }

            _logger.LogInformation("Delete project {ProjectId}, Deleted={Deleted}", projectId, result.IsSuccess);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Delete project {ProjectId} was canceled", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete project {ProjectId}", projectId);
            _snackbar.ShowError("删除项目失败，详细信息已写入日志。", "项目");
        }
        finally
        {
            IsProjectOperationInProgress = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartProjectOperation))]
    private async Task EnterProjectAsync(Guid projectId, CancellationToken cancellationToken)
    {
        IsProjectOperationInProgress = true;
        try
        {
            var projectResult = await _mediator.Send(new GetProjectByIdQuery(projectId), cancellationToken);
            if (projectResult.IsFailure)
            {
                _snackbar.ShowError(projectResult.Error ?? "加载项目失败。", "项目");
                return;
            }

            if (projectResult.Value is null)
            {
                _snackbar.ShowWarning("未找到项目。", "项目");
                return;
            }

            var project = projectResult.Value;

            if (!ProjectCapabilityCatalog.IsActive(project.Type))
            {
                _snackbar.ShowWarning(new ProjectTypeUnsupportedException(project.Type).Message, "项目");
                return;
            }

            _workspaceSession.EnterProject(project);
            _navigation.Navigate(AppRoute.Gallery);
            _logger.LogInformation("Entered project {ProjectId}", project.Id);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Enter project {ProjectId} was canceled", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enter project {ProjectId}", projectId);
            _snackbar.ShowError("加载项目失败，详细信息已写入日志。", "项目");
        }
        finally
        {
            IsProjectOperationInProgress = false;
        }
    }

    public async Task RefreshProjectsAsync(CancellationToken cancellationToken = default)
    {
        var overviewResult = await _mediator.Send(new GetDashboardProjectsQuery(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (overviewResult.IsFailure || overviewResult.Value is null)
        {
            _snackbar.ShowError(overviewResult.Error ?? "加载项目列表失败。", "项目");
            return;
        }

        ApplyProjectSnapshot(overviewResult.Value);
        await LoadProjectThumbnailsAsync(Projects.ToArray(), cancellationToken);
    }

    public void UpsertProject(Project project)
    {
        var existing = Projects.FirstOrDefault(row => row.Id == project.Id);
        if (existing is null)
        {
            Projects.Add(ProjectRow.CreateFromProject(project, project.SampleCount, project.LabelCount, project.Progress));
        }
        else
        {
            // 原地更新标量字段，保留已加载缩略图，不重建容器。
            existing.ApplyFromProject(project, project.SampleCount, project.LabelCount, project.Progress);
        }

        var desired = Projects.OrderByDescending(row => row.LastUpdatedSortKey).ToArray();
        CollectionReconciler.Reconcile(Projects, desired);
        RefreshDashboardMetrics();
        RefreshFilteredProjects();
        ProjectSnapshotChanged?.Invoke();
    }

    public void RemoveProject(Guid projectId)
    {
        var row = Projects.FirstOrDefault(project => project.Id == projectId);
        if (row is null)
        {
            return;
        }

        Projects.Remove(row);
        RefreshDashboardMetrics();
        RefreshFilteredProjects();
        ProjectSnapshotChanged?.Invoke();
    }

    public bool HasDuplicateProjectName(string name, Guid? currentProjectId)
    {
        var normalized = name.Trim();
        return Projects.Any(project =>
            project.Id != currentProjectId
            && string.Equals(project.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    partial void OnKeywordChanged(string value)
    {
        // 搜索词 250ms 防抖，避免逐键重建过滤集合。
        _keywordFilterCts?.Cancel();
        _keywordFilterCts?.Dispose();
        var cts = new CancellationTokenSource();
        _keywordFilterCts = cts;
        _ = DebounceFilteredRefreshAsync(cts);
    }

    partial void OnSelectedTypeChanged(string value) => RefreshFilteredProjects();

    partial void OnProjectCountChanged(int value) => OnPropertyChanged(nameof(ResultSummary));

    partial void OnFilteredProjectCountChanged(int value) => OnPropertyChanged(nameof(ResultSummary));

    partial void OnHasProjectsChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }

    partial void OnHasFilteredProjectsChanged(bool value) => OnPropertyChanged(nameof(HasEmptyState));

    partial void OnIsLoadingChanged(bool value) => NotifyProjectCommandsCanExecuteChanged();

    partial void OnIsProjectOperationInProgressChanged(bool value) => NotifyProjectCommandsCanExecuteChanged();

    private void ApplyProjectSnapshot(IReadOnlyList<DashboardProjectDto> dtos)
    {
        var existingById = new Dictionary<Guid, ProjectRow>(Projects.Count);
        foreach (var row in Projects)
        {
            existingById[row.Id] = row;
        }

        var desired = new List<ProjectRow>(dtos.Count);
        foreach (var dto in dtos)
        {
            if (existingById.TryGetValue(dto.Id, out var existing))
            {
                // 复用同一实例原地更新，容器与已加载缩略图保持不变。
                existing.ApplyFromDto(dto);
                desired.Add(existing);
            }
            else
            {
                desired.Add(ProjectRow.CreateFromDto(dto));
            }
        }

        CollectionReconciler.Reconcile(Projects, desired);
        RefreshDashboardMetrics();
        RefreshFilteredProjects();
        ProjectSnapshotChanged?.Invoke();
    }

    private void RefreshDashboardMetrics()
    {
        var active = Projects.ToArray();
        ProjectCount = active.Length;
        TotalSamples = active.Sum(project => project.SampleCount);
        TotalLabels = active.Sum(project => project.LabelCount);
        HasProjects = Projects.Count > 0;
        OnPropertyChanged(nameof(ResultSummary));
    }

    private void RefreshFilteredProjects()
    {
        var keyword = Keyword.Trim();
        var selectedType = SelectedType.Trim();

        var matching = Projects
            .Where(project => string.IsNullOrWhiteSpace(keyword)
                || project.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || project.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Where(project => string.IsNullOrWhiteSpace(selectedType)
                || string.Equals(project.TypeKey, selectedType, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var desired = matching;
        if (FilteredProjects.Count == 0 && desired.Length > 0)
        {
            // 从空到有：整表替换触发完整容器生成，入场动画只在此处播放一次。
            FilteredProjects = new ObservableCollection<ProjectRow>(desired);
        }
        else
        {
            // 常规过滤/刷新：增量 reconcile，复用同一实例避免重建容器。
            CollectionReconciler.Reconcile(FilteredProjects, desired);
        }

        FilteredProjectCount = FilteredProjects.Count;
        HasFilteredProjects = FilteredProjectCount > 0;
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

        RefreshFilteredProjects();
    }

    /// <summary>
    /// 将 target 增量调整为 desired 的顺序与内容：同实例（引用相等）的条目原地保留，
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

    public void SetBlockedByTransfer(bool blocked)
    {
        if (IsBlockedByTransfer == blocked)
        {
            return;
        }

        IsBlockedByTransfer = blocked;
        NotifyProjectCommandsCanExecuteChanged();
    }

    private void NotifyProjectCommandsCanExecuteChanged()
    {
        OnPropertyChanged(nameof(CanStartProjectOperation));
        ConfirmDeleteProjectCommand.NotifyCanExecuteChanged();
        DeleteProjectCommand.NotifyCanExecuteChanged();
        EnterProjectCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadProjectThumbnailsAsync(
        IReadOnlyList<ProjectRow> rows,
        CancellationToken cancellationToken)
    {
        // 已有缩略图或路径为空的跳过，避免重复解码。
        var pending = rows
            .Where(row => row.Thumbnail is null && !string.IsNullOrWhiteSpace(row.ThumbnailPath))
            .ToArray();
        if (pending.Length == 0)
        {
            return;
        }

        using var concurrencyGate = new SemaphoreSlim(4);
        var tasks = pending.Select(async row =>
        {
            var imagePath = row.ThumbnailPath;
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            await concurrencyGate.WaitAsync(cancellationToken);
            try
            {
                var bytes = await _sampleThumbnails.GetThumbnailAsync(
                    imagePath,
                    480,
                    cancellationToken);
                if (bytes is null)
                {
                    return null;
                }

                return _imageSources.FromBytes(bytes);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load dashboard thumbnail for project {ProjectId}", row.Id);
                return null;
            }
            finally
            {
                concurrencyGate.Release();
            }
        });

        var thumbnails = await Task.WhenAll(tasks);
        cancellationToken.ThrowIfCancellationRequested();

        // 原地写回同一行实例（不重建容器）；仅当行仍存在于当前列表时才写入。
        for (var i = 0; i < pending.Length; i++)
        {
            var thumbnail = thumbnails[i];
            if (thumbnail is null)
            {
                continue;
            }

            var row = pending[i];
            if (Projects.Contains(row))
            {
                row.Thumbnail = thumbnail;
            }
        }
    }
}
