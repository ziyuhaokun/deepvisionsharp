using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using System.Collections.ObjectModel;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;

namespace DeepVisionSharp.Shell.ViewModels.Models;

/// <summary>
/// Model metadata editor. Models are produced by training and saved from the training page;
/// this view model only edits name, note and the frozen class list.
/// </summary>
public sealed partial class ModelsEditorViewModel : ObservableObject, IDisposable
{
    private readonly IMediator _mediator;
    private readonly ModelsListViewModel _list;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;

    public ModelsEditorViewModel(
        IMediator mediator,
        ModelsListViewModel list,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService)
    {
        _mediator = mediator;
        _list = list;
        _snackbar = snackbar;
        _appDialogService = appDialogService;

        _list.ModelsChanged += OnListModelsChanged;
    }

    [ObservableProperty]
    private Guid? _editingModelId;

    [ObservableProperty]
    private string _editingModelStatus = string.Empty;

    [ObservableProperty]
    private string _draftName = string.Empty;

    [ObservableProperty]
    private string _draftModelType = ModelTypeNames.Default;

    [ObservableProperty]
    private string _draftNote = string.Empty;

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    /// <summary>Read-only class chips for edit dialog (not shown on model cards).</summary>
    public ObservableCollection<string> EditingClasses { get; } = new();

    public bool HasEditingClasses => IsEditingModel && EditingClasses.Count > 0;

    public string EditingClassesCountText =>
        EditingClasses.Count == 0 ? "0 类" : $"{EditingClasses.Count} 类";

    public bool CanSubmitEditModel =>
        _list.HasProjectContext
        && EditingModelId is not null
        && !string.IsNullOrWhiteSpace(DraftName);

    public bool IsEditingModel => EditingModelId is not null;

    /// <summary>编辑时只读展示；模型类型在创建后不可改。</summary>
    public string DraftModelTypeDisplay => ModelTypeNames.GetDisplayLabel(DraftModelType);

    public bool PrepareEditDraft(ModelRow row)
    {
        EditingModelId = row.Id;
        EditingModelStatus = row.Status;
        SetEditingClasses(row.Classes);
        DraftName = row.Name;
        DraftModelType = row.ModelType;
        DraftNote = string.Equals(row.Description, "暂无描述", StringComparison.Ordinal)
            ? string.Empty
            : row.Description;
        ClearDialogError();
        NotifyModelTypeUiChanged();
        return true;
    }

    [RelayCommand]
    private async Task ShowEditModelDialogAsync(ModelRow row)
    {
        if (!PrepareEditDraft(row))
        {
            return;
        }

        while (true)
        {
            var result = await _appDialogService.ShowAsync(AppDialogKind.EditModel, this);
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
        EditingModelId = null;
        EditingModelStatus = string.Empty;
        SetEditingClasses(null);
        DraftName = string.Empty;
        DraftModelType = ModelTypeNames.Default;
        DraftNote = string.Empty;
        ClearDialogError();
        NotifyModelTypeUiChanged();
    }

    private void SetEditingClasses(IReadOnlyList<string>? classes)
    {
        EditingClasses.Clear();
        if (classes is null)
        {
            return;
        }

        foreach (var name in classes)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                EditingClasses.Add(name.Trim());
            }
        }
    }

    public async Task<bool> TrySaveEditFromDraftAsync()
    {
        if (_list.CurrentProjectId is null || EditingModelId is null)
        {
            SetDialogError("未选择项目。");
            return false;
        }

        if (!CanSubmitEditModel)
        {
            SetDialogError("请输入模型名称。");
            return false;
        }

        var classes = _list.ProjectClassNames.Count > 0
            ? _list.ProjectClassNames.ToList()
            : _list.Models.FirstOrDefault(item => item.Id == EditingModelId)?.Classes.ToList() ?? [];

        // 模型类型在创建后不可修改，编辑仅更新名称/备注等元数据。
        var result = await _mediator.Send(new UpdateModelCommand(
            _list.CurrentProjectId.Value,
            EditingModelId.Value,
            Name: DraftName.Trim(),
            Classes: classes,
            Note: DraftNote.Trim()));

        if (result.IsFailure)
        {
            _snackbar.ShowError(result.Error ?? "保存模型失败", "模型");
            return false;
        }

        CancelEditDraft();
        _snackbar.ShowSuccess("模型已更新。", "模型");
        await _list.RefreshModelsAsync();
        return true;
    }

    partial void OnDraftNameChanged(string value) => NotifyEditAvailabilityChanged();

    partial void OnDraftModelTypeChanged(string value) =>
        OnPropertyChanged(nameof(DraftModelTypeDisplay));

    partial void OnEditingModelIdChanged(Guid? value)
    {
        NotifyModelTypeUiChanged();
        NotifyEditAvailabilityChanged();
    }

    private void OnListModelsChanged() => NotifyEditAvailabilityChanged();

    private void NotifyModelTypeUiChanged()
    {
        OnPropertyChanged(nameof(DraftModelTypeDisplay));
        OnPropertyChanged(nameof(IsEditingModel));
        OnPropertyChanged(nameof(HasEditingClasses));
        OnPropertyChanged(nameof(EditingClassesCountText));
    }

    private void NotifyEditAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanSubmitEditModel));
        OnPropertyChanged(nameof(IsEditingModel));
        NotifyModelTypeUiChanged();
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

    public void Dispose()
    {
        _list.ModelsChanged -= OnListModelsChanged;
    }
}
