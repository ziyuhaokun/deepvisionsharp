using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;

namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public sealed partial class GalleryEditorViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly GalleryListViewModel _list;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly ILogger<GalleryEditorViewModel> _logger;

    public GalleryEditorViewModel(
        IMediator mediator,
        GalleryListViewModel list,
        WorkspaceSessionService workspaceSession,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ILogger<GalleryEditorViewModel> logger)
    {
        _mediator = mediator;
        _list = list;
        _workspaceSession = workspaceSession;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _logger = logger;
        _list.BusyStateAffectingCommandsChanged += NotifyBusyCommands;
    }

    [ObservableProperty]
    private string _draftName = string.Empty;

    [ObservableProperty]
    private string _draftDescription = string.Empty;

    [ObservableProperty]
    private Guid? _editingGalleryId;

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isSavingGallery;

    [ObservableProperty]
    private bool _isEditingSystemGallery;

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    public bool CanSubmitCreateGallery => !IsSavingGallery && !string.IsNullOrWhiteSpace(DraftName);

    public bool CanSubmitEditGallery =>
        !IsSavingGallery && EditingGalleryId is not null && !string.IsNullOrWhiteSpace(DraftName);

    public void PrepareCreateDraft()
    {
        EditingGalleryId = null;
        DraftName = $"新建图库 {_list.GalleryCount + 1}";
        DraftDescription = string.Empty;
        ClearDialogError();
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ShowCreateGalleryDialogAsync()
    {
        PrepareCreateDraft();

        while (true)
        {
            var result = await _appDialogService.ShowAsync(AppDialogKind.CreateGallery, this);
            if (result != AppDialogResult.Primary)
            {
                return;
            }

            if (await TryCreateGalleryFromDraftAsync())
            {
                return;
            }
        }
    }

    public async Task<bool> TryCreateGalleryFromDraftAsync()
    {
        if (_list.CurrentProjectId is null)
        {
            SetDialogError("请先选择项目。");
            return false;
        }

        if (!CanSubmitCreateGallery)
        {
            SetDialogError("请先填写图库名称。");
            return false;
        }

        if (IsSavingGallery)
        {
            return false;
        }

        IsSavingGallery = true;
        try
        {
            var result = await _mediator.Send(new CreateGalleryCommand(
                _list.CurrentProjectId.Value,
                DraftName,
                DraftDescription));
            if (result.IsFailure || result.Value is null)
            {
                SetDialogError(result.Error ?? "创建图库失败。");
                return false;
            }

            _list.InsertGallery(GalleryRow.FromGallery(result.Value));
            _snackbar.ShowSuccess($"已创建图库「{result.Value.Name}」。", "图库");
            _logger.LogInformation("Created gallery {GalleryId}", result.Value.Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create gallery");
            SetDialogError("创建图库失败，详细信息已写入日志。");
            return false;
        }
        finally
        {
            IsSavingGallery = false;
        }
    }

    public bool PrepareEditDraft(Guid galleryId)
    {
        var row = _list.Galleries.FirstOrDefault(gallery => gallery.Id == galleryId);
        if (row is null)
        {
            _snackbar.ShowWarning("未找到图库。", "图库");
            return false;
        }

        EditingGalleryId = row.Id;
        DraftName = row.Name;
        DraftDescription = row.Description;
        IsEditingSystemGallery = false;
        ClearDialogError();
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ShowEditGalleryDialogAsync(Guid galleryId)
    {
        if (!PrepareEditDraft(galleryId))
        {
            return;
        }

        while (true)
        {
            var result = await _appDialogService.ShowAsync(AppDialogKind.EditGallery, this);
            if (result != AppDialogResult.Primary)
            {
                CancelEditDraft();
                return;
            }

            if (await TrySaveEditFromDraftAsync())
            {
                return;
            }
        }
    }

    public void CancelEditDraft()
    {
        EditingGalleryId = null;
        DraftName = string.Empty;
        DraftDescription = string.Empty;
        IsEditingSystemGallery = false;
        ClearDialogError();
    }

    public async Task<bool> TrySaveEditFromDraftAsync()
    {
        if (!CanSubmitEditGallery || EditingGalleryId is null)
        {
            SetDialogError("请先填写图库名称。");
            return false;
        }

        if (IsSavingGallery)
        {
            return false;
        }

        IsSavingGallery = true;
        try
        {
            var result = await _mediator.Send(new UpdateGalleryCommand(EditingGalleryId.Value, DraftName, DraftDescription));
            if (result.IsFailure || result.Value is null)
            {
                SetDialogError(result.Error ?? "保存图库失败。");
                return false;
            }

            _list.ReplaceGallery(GalleryRow.FromGallery(result.Value));
            CancelEditDraft();
            _snackbar.ShowSuccess($"已更新图库「{result.Value.Name}」。", "图库");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update gallery {GalleryId}", EditingGalleryId);
            SetDialogError("保存图库失败，详细信息已写入日志。");
            return false;
        }
        finally
        {
            IsSavingGallery = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ConfirmDeleteGalleryAsync(Guid galleryId)
    {
        var gallery = _list.Galleries.FirstOrDefault(item => item.Id == galleryId);
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到图库。", "图库");
            return;
        }

        var request = new ConfirmDialogRequest(
            $"即将删除图库「{gallery.Name}」及其样本与标注数据。此操作不可恢复。");
        var result = await _appDialogService.ShowAsync(
            AppDialogKind.Confirm,
            request,
            new DialogOptions(
                Title: "确认删除图库",
                PrimaryButtonText: "删除",
                DefaultButton: AppDialogDefaultButton.Close));

        if (result == AppDialogResult.Primary)
        {
            await DeleteGalleryCommand.ExecuteAsync(galleryId);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task DeleteGalleryAsync(Guid galleryId)
    {
        IsSavingGallery = true;
        try
        {
            var deleteResult = await _mediator.Send(new DeleteGalleryCommand(galleryId));
            if (deleteResult.IsFailure)
            {
                _snackbar.ShowWarning(deleteResult.Error ?? "未找到要删除的图库。", "图库");
                _logger.LogWarning("Delete gallery failed for {GalleryId}: {Error}", galleryId, deleteResult.Error);
                return;
            }

            _list.RemoveGallery(galleryId);
            _workspaceSession.ClearGalleryIfCurrent(galleryId);
            _snackbar.ShowSuccess("图库已从当前迁移数据集中删除。", "图库");
            _logger.LogInformation("Deleted gallery {GalleryId}", galleryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete gallery {GalleryId}", galleryId);
            _snackbar.ShowError("删除图库失败，详细信息已写入日志。", "图库");
        }
        finally
        {
            IsSavingGallery = false;
        }
    }

    private bool CanRunGalleryOperation() => !IsSavingGallery && _list.CanRunGalleryOperation();

    private void NotifyBusyCommands()
    {
        DeleteGalleryCommand.NotifyCanExecuteChanged();
        ConfirmDeleteGalleryCommand.NotifyCanExecuteChanged();
        ShowCreateGalleryDialogCommand.NotifyCanExecuteChanged();
        ShowEditGalleryDialogCommand.NotifyCanExecuteChanged();
    }

    partial void OnDraftNameChanged(string value)
    {
        OnPropertyChanged(nameof(CanSubmitCreateGallery));
        OnPropertyChanged(nameof(CanSubmitEditGallery));
    }

    partial void OnIsSavingGalleryChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSubmitCreateGallery));
        OnPropertyChanged(nameof(CanSubmitEditGallery));
        _list.NotifyBusyStateChanged();
        NotifyBusyCommands();
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
