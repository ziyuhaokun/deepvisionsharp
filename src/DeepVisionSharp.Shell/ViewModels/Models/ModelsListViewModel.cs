using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;

namespace DeepVisionSharp.Shell.ViewModels.Models;

public sealed partial class ModelsListViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly ILocalFileDialogService _fileDialog;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly IModelCheckpointFileService _checkpointFiles;
    private readonly IProcessLauncher _processLauncher;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly ITaskCapabilityResolver _taskCapabilities;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger<ModelsListViewModel> _logger;

    public ModelsListViewModel(
        IMediator mediator,
        ILocalFileDialogService fileDialog,
        WorkspaceSessionService workspaceSession,
        IModelCheckpointFileService checkpointFiles,
        IProcessLauncher processLauncher,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ITaskCapabilityResolver taskCapabilities,
        IUiDispatcher uiDispatcher,
        ILogger<ModelsListViewModel> logger)
    {
        _mediator = mediator;
        _fileDialog = fileDialog;
        _workspaceSession = workspaceSession;
        _checkpointFiles = checkpointFiles;
        _processLauncher = processLauncher;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _taskCapabilities = taskCapabilities;
        _uiDispatcher = uiDispatcher;
        _logger = logger;

        Models.CollectionChanged += OnModelsChanged;
        StatusOptions.Add(new ModelStatusOption(string.Empty, "全部状态"));
        StatusOptions.Add(new ModelStatusOption("创建中", "创建中"));
        StatusOptions.Add(new ModelStatusOption("待训练", "待训练"));
        StatusOptions.Add(new ModelStatusOption("训练中", "训练中"));
        StatusOptions.Add(new ModelStatusOption("训练完成", "训练完成"));
        StatusOptions.Add(new ModelStatusOption("已发布", "已发布"));
        StatusOptions.Add(new ModelStatusOption("归档", "归档"));
    }

    public ObservableCollection<ModelRow> Models { get; } = new();

    public ObservableCollection<ModelRow> FilteredModels { get; } = new();

    public ObservableCollection<ModelStatusOption> StatusOptions { get; } = new();

    public ObservableCollection<string> ProjectClassNames { get; } = new();

    [ObservableProperty]
    private string _projectName = "模型";

    [ObservableProperty]
    private string _projectDescription = "请先在项目面板选择项目。";

    [ObservableProperty]
    private bool _hasProjectContext;

    [ObservableProperty]
    private string _keyword = string.Empty;

    [ObservableProperty]
    private string _selectedStatus = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _modelCount;

    [ObservableProperty]
    private int _filteredModelCount;

    [ObservableProperty]
    private bool _hasModels;

    [ObservableProperty]
    private bool _hasFilteredModels;

    public Guid? CurrentProjectId { get; private set; }

    public string CurrentProjectType { get; private set; } = string.Empty;

    [ObservableProperty]
    private ProjectCapabilityProfile? _projectProfile;

    public bool HasProjectClasses => ProjectClassNames.Count > 0;

    public bool IsClassificationProject => ProjectProfile is not null;

    public string ProjectClassesSummary => HasProjectClasses
        ? string.Join(" / ", ProjectClassNames)
        : "当前项目尚未配置类别，请先在标注页添加后再创建模型。";

    public string EmptyStateTitle => HasModels ? "未找到匹配模型" : HasProjectContext ? "暂无模型" : "未选择项目";

    public string EmptyStateDescription => HasModels
        ? "尝试调整搜索关键词或筛选条件"
        : !HasProjectContext
            ? "请先从项目页进入一个项目，再查看模型库。"
            : "模型由训练任务生成：请到训练页创建并运行训练，完成后点击「保存为模型」。";

    public bool HasEmptyState => !HasFilteredModels;

    public string ResultSummary => $"{FilteredModelCount} / {ModelCount} 个模型";

    public event Action? ModelsChanged;

    public void Dispose()
    {
        Models.CollectionChanged -= OnModelsChanged;
    }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        try
        {
            await RefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Models page load was canceled");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Keyword = string.Empty;
        SelectedStatus = string.Empty;
    }

    /// <summary>导出独立 PyTorch 包（<c>.pt</c> + <c>.pt.info</c>），不夹带 IR 目录。</summary>
    [RelayCommand]
    private async Task ExportModelAsync(Guid modelId)
    {
        var model = Models.FirstOrDefault(item => item.Id == modelId);
        if (model is null)
        {
            _snackbar.ShowWarning("未找到模型。", "模型");
            return;
        }

        if (!_checkpointFiles.Exists(model.CheckpointPath))
        {
            _snackbar.ShowWarning("模型文件不存在，无法导出。", "模型");
            return;
        }

        var exportRoot = await _fileDialog.SelectFolderAsync();
        if (string.IsNullOrWhiteSpace(exportRoot))
        {
            _snackbar.ShowInfo("已取消导出。", "模型");
            return;
        }

        var exportDirectory = _checkpointFiles.Export(model.CheckpointPath, exportRoot, model.Name);
        if (exportDirectory is null)
        {
            _snackbar.ShowError("导出模型文件失败。", "模型");
            return;
        }

        _snackbar.ShowSuccess($"已导出模型到 {exportDirectory}", "模型");
    }

    [RelayCommand]
    private async Task ConfirmDeleteModelAsync(Guid modelId)
    {
        if (CurrentProjectId is null)
        {
            _snackbar.ShowWarning("未选择项目。", "模型");
            return;
        }

        var model = Models.FirstOrDefault(item => item.Id == modelId);
        if (model is null)
        {
            _snackbar.ShowWarning("未找到模型。", "模型");
            return;
        }

        var request = new ConfirmDialogRequest(
            $"即将删除模型「{model.Name}」，相关模型文件也会一并删除。");
        var result = await _appDialogService.ShowAsync(
            AppDialogKind.Confirm,
            request,
            new DialogOptions(
                Title: "确认删除模型",
                PrimaryButtonText: "删除",
                DefaultButton: AppDialogDefaultButton.Close));

        if (result == AppDialogResult.Primary)
        {
            await DeleteModelCommand.ExecuteAsync(modelId);
        }
    }

    [RelayCommand]
    private async Task DeleteModelAsync(Guid modelId)
    {
        if (CurrentProjectId is null)
        {
            _snackbar.ShowWarning("未选择项目。", "模型");
            return;
        }

        var model = Models.FirstOrDefault(item => item.Id == modelId);
        if (model is null)
        {
            _snackbar.ShowWarning("未找到模型。", "模型");
            return;
        }

        var deleted = await _mediator.Send(new DeleteModelCommand(CurrentProjectId.Value, modelId));
        if (deleted.IsFailure)
        {
            _snackbar.ShowError(deleted.Error ?? "删除失败，模型可能已不存在。", "模型");
            return;
        }

        // The row is already gone; a file cleanup failure must not skip the list refresh, or the
        // deleted model stays visible until the next reload.
        try
        {
            DeleteModelFiles(model);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Deleted model row but could not remove its files");
            _snackbar.ShowWarning(
                $"已删除模型「{model.Name}」，但文件未能完全清理，可在模型目录手动删除。",
                "模型");
            await RefreshModelsAsync();
            return;
        }

        _snackbar.ShowSuccess($"已删除模型「{model.Name}」。", "模型");
        await RefreshModelsAsync();
    }

    [RelayCommand]
    private void OpenModelFolder(Guid modelId)
    {
        var model = Models.FirstOrDefault(item => item.Id == modelId);
        if (model is null || string.IsNullOrWhiteSpace(model.CheckpointPath))
        {
            _snackbar.ShowWarning("未找到模型文件目录。", "模型");
            return;
        }

        if (!_checkpointFiles.TryGetDirectory(model.CheckpointPath, out var directory)
            || !_processLauncher.TryOpenFolder(directory))
        {
            _snackbar.ShowWarning("模型目录不存在。", "模型");
            return;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var session = _workspaceSession.Current;
        var selectedProject = session.Project;
        if (selectedProject is null)
        {
            CurrentProjectId = null;
            CurrentProjectType = string.Empty;
            ProjectProfile = null;
            HasProjectContext = false;
            ProjectName = "模型";
            ProjectDescription = "请先在项目面板选择项目。";
            Models.Clear();
            RefreshProjectClassNames(Array.Empty<string>());
            return;
        }

        var latestProjectResult = await _mediator.Send(
            new GetProjectByIdQuery(selectedProject.Id),
            cancellationToken);
        if (!_workspaceSession.IsCurrent(session.Version))
        {
            return;
        }

        if (latestProjectResult.IsFailure || latestProjectResult.Value is null)
        {
            if (latestProjectResult.IsSuccess && latestProjectResult.Value is null)
            {
                _workspaceSession.ClearProjectIfCurrent(selectedProject.Id);
            }

            CurrentProjectId = null;
            CurrentProjectType = string.Empty;
            ProjectProfile = null;
            HasProjectContext = false;
            ProjectName = "模型";
            ProjectDescription = latestProjectResult.IsFailure
                ? (latestProjectResult.Error ?? "加载项目失败。")
                : "当前项目不存在或已被删除。";
            Models.Clear();
            RefreshProjectClassNames(Array.Empty<string>());
            return;
        }

        var latestProject = latestProjectResult.Value;
        var profileResult = _taskCapabilities.ForProject(latestProject.Type);
        if (profileResult.IsFailure || profileResult.Value is null)
        {
            CurrentProjectId = null;
            CurrentProjectType = string.Empty;
            ProjectProfile = null;
            HasProjectContext = false;
            ProjectName = "模型";
            ProjectDescription = profileResult.Error ?? "项目类型无效。";
            Models.Clear();
            RefreshProjectClassNames(Array.Empty<string>());
            return;
        }

        CurrentProjectId = latestProject.Id;
        CurrentProjectType = latestProject.Type;
        ProjectProfile = profileResult.Value;
        HasProjectContext = true;
        ProjectName = latestProject.Name;
        ProjectDescription = string.IsNullOrWhiteSpace(latestProject.Description)
            ? "管理当前项目的训练模型与导出文件。"
            : latestProject.Description;

        RefreshProjectClassNames(latestProject.Labels);
        await RefreshModelsAsync(session.Version, cancellationToken);
    }

    public async Task RefreshModelsAsync(
        long? expectedSessionVersion = null,
        CancellationToken cancellationToken = default)
    {
        if (CurrentProjectId is null)
        {
            Models.Clear();
            return;
        }

        await _mediator.Send(
            new SyncModelDisplayStatusesCommand(CurrentProjectId.Value),
            cancellationToken);
        var modelsResult = await _mediator.Send(
            new GetModelsByProjectQuery(CurrentProjectId.Value),
            cancellationToken);
        if (expectedSessionVersion is not null
            && !_workspaceSession.IsCurrent(expectedSessionVersion.Value))
        {
            return;
        }

        var models = modelsResult.IsSuccess && modelsResult.Value is not null
            ? modelsResult.Value
            : Array.Empty<Domain.Entities.TrainedModel>();

        var loadedModels = new List<ModelRow>();
        foreach (var model in models)
        {
            ModelDisplayMetrics? metadata = null;
            if (!string.IsNullOrWhiteSpace(model.CheckpointPath))
            {
                var metricsResult = await _mediator.Send(
                    new GetModelDisplayMetricsQuery(model.CheckpointPath),
                    cancellationToken);
                if (metricsResult.IsSuccess)
                {
                    metadata = metricsResult.Value;
                }

                if (expectedSessionVersion is not null
                    && !_workspaceSession.IsCurrent(expectedSessionVersion.Value))
                {
                    return;
                }
            }

            var classes = model.Classes
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            loadedModels.Add(new ModelRow(
                model.Id,
                model.Name,
                string.IsNullOrWhiteSpace(model.Note) ? "暂无描述" : model.Note,
                model.ModelType,
                TrainedModelStatuses.ToDisplayText(model.Status),
                classes,
                model.CheckpointPath,
                model.CreatedAt,
                FormatRelativeTime(model.CreatedAt),
                metadata?.Epochs ?? 0,
                metadata?.BestMap ?? 0,
                metadata?.BestMap50 ?? 0,
                metadata?.HasMetadata ?? false,
                metadata?.InputChannels ?? 3));
        }

        if (expectedSessionVersion is not null
            && !_workspaceSession.IsCurrent(expectedSessionVersion.Value))
        {
            return;
        }

        Models.Clear();
        foreach (var model in loadedModels)
        {
            Models.Add(model);
        }

        _logger.LogInformation("Loaded {ModelCount} models for project {ProjectId}", Models.Count, CurrentProjectId.Value);
    }

    public void RefreshProjectClassNames(IReadOnlyList<string> labels)
    {
        ProjectClassNames.Clear();
        foreach (var label in labels
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ProjectClassNames.Add(label);
        }

        OnPropertyChanged(nameof(HasProjectClasses));
        OnPropertyChanged(nameof(ProjectClassesSummary));
        OnPropertyChanged(nameof(IsClassificationProject));
        ModelsChanged?.Invoke();
    }

    partial void OnKeywordChanged(string value) => RefreshFilteredModels();

    partial void OnSelectedStatusChanged(string value) => RefreshFilteredModels();

    partial void OnHasModelsChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
        ModelsChanged?.Invoke();
    }

    partial void OnHasProjectContextChanged(bool value)
    {
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
        ModelsChanged?.Invoke();
    }

    partial void OnProjectProfileChanged(ProjectCapabilityProfile? value)
    {
        OnPropertyChanged(nameof(IsClassificationProject));
        OnPropertyChanged(nameof(EmptyStateDescription));
        ModelsChanged?.Invoke();
    }

    partial void OnIsLoadingChanged(bool value) => ModelsChanged?.Invoke();

    partial void OnHasFilteredModelsChanged(bool value) => OnPropertyChanged(nameof(HasEmptyState));

    partial void OnModelCountChanged(int value) => OnPropertyChanged(nameof(ResultSummary));

    partial void OnFilteredModelCountChanged(int value) => OnPropertyChanged(nameof(ResultSummary));

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ModelCount = Models.Count;
        HasModels = Models.Count > 0;
        RefreshFilteredModels();
        ModelsChanged?.Invoke();
    }

    private void RefreshFilteredModels()
    {
        var keyword = Keyword.Trim();
        var selectedStatus = SelectedStatus.Trim();

        var filtered = Models
            .Where(model => string.IsNullOrWhiteSpace(keyword)
                || model.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || model.Description.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .Where(model => string.IsNullOrWhiteSpace(selectedStatus)
                || string.Equals(model.Status, selectedStatus, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        FilteredModels.Clear();
        foreach (var model in filtered)
        {
            FilteredModels.Add(model);
        }

        FilteredModelCount = FilteredModels.Count;
        HasFilteredModels = FilteredModelCount > 0;
    }

    private void DeleteModelFiles(ModelRow model)
    {
        if (string.IsNullOrWhiteSpace(model.CheckpointPath))
        {
            return;
        }

        _checkpointFiles.DeleteFiles(model.CheckpointPath);
    }

    private static string FormatRelativeTime(DateTimeOffset timestamp)
    {
        var elapsed = DateTimeOffset.Now - timestamp.ToLocalTime();
        if (elapsed.TotalMinutes < 1)
        {
            return "刚刚创建";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalMinutes)} 分钟前";
        }

        if (elapsed.TotalDays < 1)
        {
            return $"{Math.Max(1, (int)elapsed.TotalHours)} 小时前";
        }

        if (elapsed.TotalDays < 7)
        {
            return $"{Math.Max(1, (int)elapsed.TotalDays)} 天前";
        }

        return timestamp.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
