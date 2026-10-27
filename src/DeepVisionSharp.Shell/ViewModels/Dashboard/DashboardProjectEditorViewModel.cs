using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;

namespace DeepVisionSharp.Shell.ViewModels.Dashboard;

public sealed partial class DashboardProjectEditorViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly DashboardProjectListViewModel _list;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly ILogger<DashboardProjectEditorViewModel> _logger;

    public DashboardProjectEditorViewModel(
        IMediator mediator,
        DashboardProjectListViewModel list,
        WorkspaceSessionService workspaceSession,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ILogger<DashboardProjectEditorViewModel> logger)
    {
        _mediator = mediator;
        _list = list;
        _workspaceSession = workspaceSession;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _logger = logger;

        _list.ProjectSnapshotChanged += OnListProjectsChanged;
    }

    [ObservableProperty]
    private string _draftName = string.Empty;

    [ObservableProperty]
    private string _draftDescription = string.Empty;

    [ObservableProperty]
    private string _draftType = ProjectCapabilityCatalog.DefaultCreationType;

    [ObservableProperty]
    private Guid? _editingProjectId;

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    [ObservableProperty]
    private string _draftNameFormatError = string.Empty;

    [ObservableProperty]
    private bool _isSavingProject;

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    public bool HasDraftNameFormatError => !string.IsNullOrWhiteSpace(DraftNameFormatError);

    /// <summary>当前选中项目类型的一句话用途说明（创建后不可改类型，故创建时即展示）。</summary>
    public string SelectedTypeDescription => ProjectTypes.GetDescription(DraftType);

    private static readonly System.Text.RegularExpressions.Regex ProjectNameRegex = new(
        @"^[\u4e00-\u9fa5a-zA-Z0-9_\-\s]+$",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public bool IsNotSavingProject => !IsSavingProject;

    public IEnumerable<ProjectTypeFilterOption> SelectableProjectTypes =>
        ProjectCapabilityCatalog.ActiveProjectTypes.Select(type => new ProjectTypeFilterOption(type, ProjectTypes.GetDisplayLabel(type)));

    public bool CanEditProjectType => EditingProjectId is null;

    public bool CanSubmitCreate =>
        IsNotSavingProject
        && !string.IsNullOrWhiteSpace(DraftName)
        && !_list.HasDuplicateProjectName(DraftName, null);

    public bool CanSubmitEdit =>
        EditingProjectId is not null
        && IsNotSavingProject
        && !string.IsNullOrWhiteSpace(DraftName)
        && !_list.HasDuplicateProjectName(DraftName, EditingProjectId);

    public void PrepareCreateDraft()
    {
        EditingProjectId = null;
        DraftName = string.Empty;
        DraftDescription = string.Empty;
        DraftType = ProjectCapabilityCatalog.DefaultCreationType;
        ClearDialogError();
        OnPropertyChanged(nameof(CanEditProjectType));
    }

    [RelayCommand]
    private void CancelCreate()
    {
        PrepareCreateDraft();
    }

    [RelayCommand(CanExecute = nameof(IsNotSavingProject))]
    private async Task ShowCreateProjectDialogAsync()
    {
        PrepareCreateDraft();

        while (true)
        {
            var result = await _appDialogService.ShowAsync(AppDialogKind.CreateProject, this);
            if (result != AppDialogResult.Primary)
            {
                CancelCreate();
                return;
            }

            if (await TryCreateProjectFromDraftAsync())
            {
                return;
            }
        }
    }

    public async Task<bool> TryCreateProjectFromDraftAsync()
    {
        if (!CanSubmitCreate)
        {
            if (_list.HasDuplicateProjectName(DraftName, null))
            {
                SetDialogError($"项目名称「{DraftName.Trim()}」已存在。");
            }
            else
            {
                SetDialogError("请先填写项目名称。");
            }

            return false;
        }

        return await CreateProjectCoreAsync();
    }

    private async Task<bool> CreateProjectCoreAsync()
    {
        if (IsSavingProject)
        {
            return false;
        }

        IsSavingProject = true;
        try
        {
            var result = await _mediator.Send(new CreateProjectCommand(DraftName, DraftDescription, DraftType));
            if (result.IsFailure || result.Value is null)
            {
                SetDialogError(result.Error ?? "创建项目失败。");
                return false;
            }

            _list.UpsertProject(result.Value);
            _snackbar.ShowSuccess($"已创建项目「{result.Value.Name}」。", "项目");
            _logger.LogInformation("Created project {ProjectId}", result.Value.Id);
            CancelCreate();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create project");
            SetDialogError("创建项目失败，详细信息已写入日志。");
            return false;
        }
        finally
        {
            IsSavingProject = false;
        }
    }

    public bool PrepareEditDraft(Guid projectId)
    {
        var row = _list.Projects.FirstOrDefault(project => project.Id == projectId);
        if (row is null)
        {
            _snackbar.ShowWarning("未找到项目。", "项目");
            return false;
        }

        EditingProjectId = row.Id;
        DraftName = row.Name;
        DraftDescription = row.Description == "暂无描述" ? string.Empty : row.Description;
        DraftType = row.TypeKey;
        ClearDialogError();
        OnPropertyChanged(nameof(CanEditProjectType));
        return true;
    }

    [RelayCommand(CanExecute = nameof(IsNotSavingProject))]
    private async Task ShowEditProjectDialogAsync(Guid projectId)
    {
        if (!PrepareEditDraft(projectId))
        {
            return;
        }

        while (true)
        {
            var result = await _appDialogService.ShowAsync(AppDialogKind.EditProject, this);
            if (result != AppDialogResult.Primary)
            {
                CancelEdit();
                return;
            }

            if (await TrySaveEditFromDraftAsync())
            {
                return;
            }
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        EditingProjectId = null;
        DraftName = string.Empty;
        DraftDescription = string.Empty;
        DraftType = ProjectCapabilityCatalog.DefaultCreationType;
        ClearDialogError();
        OnPropertyChanged(nameof(CanEditProjectType));
    }

    public async Task<bool> TrySaveEditFromDraftAsync()
    {
        if (!CanSubmitEdit)
        {
            if (EditingProjectId is null)
            {
                SetDialogError("未找到项目。");
            }
            else if (_list.HasDuplicateProjectName(DraftName, EditingProjectId))
            {
                SetDialogError($"项目名称「{DraftName.Trim()}」已存在。");
            }
            else
            {
                SetDialogError("请先填写项目名称。");
            }

            return false;
        }

        return await SaveEditCoreAsync();
    }

    private async Task<bool> SaveEditCoreAsync()
    {
        if (EditingProjectId is null)
        {
            return false;
        }

        if (IsSavingProject)
        {
            return false;
        }

        IsSavingProject = true;
        try
        {
            var projectId = EditingProjectId.Value;
            var result = await _mediator.Send(new UpdateProjectCommand(projectId, DraftName, DraftDescription, DraftType));
            if (result.IsFailure || result.Value is null)
            {
                SetDialogError(result.Error ?? "项目不存在，无法保存。");
                return false;
            }

            _list.UpsertProject(result.Value);
            _snackbar.ShowSuccess($"已更新项目「{result.Value.Name}」。", "项目");
            _logger.LogInformation("Updated project {ProjectId}", projectId);
            _workspaceSession.TryUpdateCurrentProject(result.Value);
            CancelEdit();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update project {ProjectId}", EditingProjectId);
            SetDialogError("保存项目失败，详细信息已写入日志。");
            return false;
        }
        finally
        {
            IsSavingProject = false;
        }
    }

    partial void OnDraftNameChanged(string value)
    {
        ValidateDraftNameFormat(value);
        OnPropertyChanged(nameof(CanSubmitCreate));
        OnPropertyChanged(nameof(CanSubmitEdit));
    }

    partial void OnDraftTypeChanged(string value) => OnPropertyChanged(nameof(SelectedTypeDescription));

    partial void OnDraftNameFormatErrorChanged(string value) => OnPropertyChanged(nameof(HasDraftNameFormatError));

    /// <summary>
    /// 名称格式即时校验（长度 / 字符集，与 CreateProjectCommandValidator 口径一致）。
    /// 空名不做即时提示（提交时才提示），避免对话框一打开就报错。
    /// </summary>
    private void ValidateDraftNameFormat(string value)
    {
        var trimmed = value.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            DraftNameFormatError = string.Empty;
        }
        else if (trimmed.Length > 100)
        {
            DraftNameFormatError = "项目名称不能超过100个字符";
        }
        else if (!ProjectNameRegex.IsMatch(trimmed))
        {
            DraftNameFormatError = "项目名称只能包含中文、字母、数字、下划线、连字符和空格";
        }
        else
        {
            DraftNameFormatError = string.Empty;
        }
    }

    private void OnListProjectsChanged()
    {
        OnPropertyChanged(nameof(CanSubmitCreate));
        OnPropertyChanged(nameof(CanSubmitEdit));
    }

    partial void OnIsSavingProjectChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotSavingProject));
        OnPropertyChanged(nameof(CanSubmitCreate));
        OnPropertyChanged(nameof(CanSubmitEdit));
        ShowCreateProjectDialogCommand.NotifyCanExecuteChanged();
        ShowEditProjectDialogCommand.NotifyCanExecuteChanged();
    }

    private void SetDialogError(string message)
    {
        DialogErrorMessage = message;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    private void ClearDialogError()
    {
        DialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }
}
