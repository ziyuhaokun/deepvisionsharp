using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;
using System.ComponentModel;

using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

public sealed partial class LabelingLabelsViewModel : ObservableObject
{
    private readonly LabelingWorkspaceContext _context;
    private readonly IProjectLabelService _labels;
    private readonly LabelingEditorViewModel _editor;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly ILogger<LabelingLabelsViewModel> _logger;

    public LabelingLabelsViewModel(
        LabelingWorkspaceContext context,
        IProjectLabelService labels,
        LabelingEditorViewModel editor,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ILogger<LabelingLabelsViewModel> logger)
    {
        _context = context;
        _labels = labels;
        _editor = editor;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _logger = logger;

        _context.PropertyChanged += OnContextPropertyChanged;
        _editor.PropertyChanged += OnEditorPropertyChanged;
    }

    public bool CanManageLabels => _context.IsClassificationProject;

    public ObservableCollection<LabelOptionRow> LabelOptions { get; } = new();

    public IReadOnlyList<string> PaletteColors => LabelingConstants.Palette;

    [ObservableProperty]
    private LabelOptionRow? _selectedLabel;

    [ObservableProperty]
    private string _draftLabelName = string.Empty;

    [ObservableProperty]
    private string _draftLabelColor = LabelingConstants.Palette[0];

    [ObservableProperty]
    private string _dialogErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isEditLabelColorMode;

    private LabelOptionRow? _editingLabel;

    public bool HasDialogErrorMessage => !string.IsNullOrWhiteSpace(DialogErrorMessage);

    public bool IsLabelNameEditable => !IsEditLabelColorMode;

    public string ActiveLabelName => SelectedLabel?.Name ?? "object";

    public string ActiveLabelColor => SelectedLabel?.Color ?? LabelingConstants.Palette[0];

    public Guid ActiveLabelId => SelectedLabel?.Id ?? Guid.Empty;

    /// <summary>Image class persisted for the sample currently open in the editor.</summary>
    public string? CurrentImageClass => _editor.ImageClass;

    public bool HasCurrentImageClass => !string.IsNullOrWhiteSpace(CurrentImageClass);

    public string CurrentImageClassDisplay => HasCurrentImageClass ? CurrentImageClass! : "未标注";

    public string CurrentImageClassColor => LabelOptions
        .FirstOrDefault(item => string.Equals(item.Name, CurrentImageClass, StringComparison.OrdinalIgnoreCase))
        ?.Color ?? "#64748B";

    [RelayCommand(CanExecute = nameof(CanManageLabel))]
    private async Task SelectLabelAsync(LabelOptionRow label)
    {
        SelectedLabel = label;
        await _editor.ApplyImageClassAndSaveAsync(label.Name);
    }

    [RelayCommand(CanExecute = nameof(CanManageLabel))]
    private async Task DeleteLabelAsync(LabelOptionRow label)
    {
        if (_context.Gallery is null)
        {
            return;
        }

        await _labels.RemoveAsync(_context.Gallery.ProjectId, label.Name, purgeAnnotations: true);
        LabelOptions.Remove(label);
        SelectedLabel = LabelOptions.FirstOrDefault();
        await ReloadCurrentSampleAnnotationsAsync();
        _snackbar.ShowSuccess($"已删除类别「{label.Name}」并清理关联标注。", "标注");
    }

    [RelayCommand(CanExecute = nameof(CanManageLabel))]
    private async Task ShowAddLabelDialogAsync()
    {
        PrepareAddLabelDraft();

        while (true)
        {
            var result = await _appDialogService.ShowAsync(AppDialogKind.AddLabel, this);
            if (result != AppDialogResult.Primary)
            {
                CancelAddLabelDraft();
                return;
            }

            if (await TryAddLabelFromDraftAsync())
            {
                return;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanManageLabel))]
    private async Task ShowEditLabelColorDialogAsync(LabelOptionRow label)
    {
        PrepareEditLabelColorDraft(label);

        while (true)
        {
            var result = await _appDialogService.ShowAsync(AppDialogKind.EditLabelColor, this);
            if (result != AppDialogResult.Primary)
            {
                CancelAddLabelDraft();
                return;
            }

            if (await TryUpdateLabelColorFromDraftAsync())
            {
                return;
            }
        }
    }

    public void PrepareAddLabelDraft()
    {
        _editingLabel = null;
        IsEditLabelColorMode = false;
        DraftLabelName = string.Empty;
        DraftLabelColor = LabelingConstants.Palette[LabelOptions.Count % LabelingConstants.Palette.Length];
        DialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
        OnPropertyChanged(nameof(IsLabelNameEditable));
    }

    public void PrepareEditLabelColorDraft(LabelOptionRow label)
    {
        _editingLabel = label;
        IsEditLabelColorMode = true;
        DraftLabelName = label.Name;
        DraftLabelColor = string.IsNullOrWhiteSpace(label.Color)
            ? LabelingConstants.Palette[0]
            : label.Color;
        DialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
        OnPropertyChanged(nameof(IsLabelNameEditable));
    }

    public void CancelAddLabelDraft()
    {
        _editingLabel = null;
        IsEditLabelColorMode = false;
        DraftLabelName = string.Empty;
        DialogErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
        OnPropertyChanged(nameof(IsLabelNameEditable));
    }

    public async Task<bool> TryAddLabelFromDraftAsync()
    {
        if (_context.Gallery is null)
        {
            SetDialogError("请先选择图库。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(DraftLabelName))
        {
            SetDialogError("类别名称不能为空。");
            return false;
        }

        var trimmedName = DraftLabelName.Trim();
        if (LabelOptions.Any(item => string.Equals(item.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            SetDialogError("类别名称已存在。");
            return false;
        }

        try
        {
            var color = string.IsNullOrWhiteSpace(DraftLabelColor)
                ? LabelingConstants.Palette[LabelOptions.Count % LabelingConstants.Palette.Length]
                : DraftLabelColor;
            var label = await _labels.AddAsync(_context.Gallery.ProjectId, trimmedName, color);
            var row = LabelOptionRow.FromOption(label, LabelOptions.Count);

            if (!LabelOptions.Any(item => string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase)))
            {
                LabelOptions.Add(row);
            }

            SelectedLabel = LabelOptions.First(item => string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase));
            DraftLabelName = string.Empty;
            DialogErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasDialogErrorMessage));
            _snackbar.ShowSuccess($"已添加类别「{row.Name}」。", "标注");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add label {LabelName}", trimmedName);
            SetDialogError($"添加失败：{ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryUpdateLabelColorFromDraftAsync()
    {
        if (_context.Gallery is null)
        {
            SetDialogError("请先选择图库。");
            return false;
        }

        if (_editingLabel is null)
        {
            SetDialogError("未选择要修改的类别。");
            return false;
        }

        var color = string.IsNullOrWhiteSpace(DraftLabelColor)
            ? LabelingConstants.Palette[0]
            : DraftLabelColor.Trim();

        if (string.Equals(_editingLabel.Color, color, StringComparison.OrdinalIgnoreCase))
        {
            CancelAddLabelDraft();
            return true;
        }

        try
        {
            var updated = await _labels.UpdateColorAsync(_context.Gallery.ProjectId, _editingLabel.Id, color);
            ReplaceLabelOption(_editingLabel, updated);
            CancelAddLabelDraft();
            _snackbar.ShowSuccess($"已更新类别「{updated.Name}」颜色。", "标注");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update label color for {LabelId}", _editingLabel.Id);
            SetDialogError($"修改颜色失败：{ex.Message}");
            return false;
        }
    }

    public async Task LoadLabelsAsync(CancellationToken cancellationToken = default)
    {
        LabelOptions.Clear();
        if (_context.Gallery is null)
        {
            return;
        }

        var labels = await _labels.ListAsync(_context.Gallery.ProjectId, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var index = 0;
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels.Where(label => !string.IsNullOrWhiteSpace(label.Name)))
        {
            if (!seenNames.Add(label.Name))
            {
                continue;
            }

            LabelOptions.Add(LabelOptionRow.FromOption(label, index++));
        }

        SelectedLabel = LabelOptions.FirstOrDefault(item =>
                     string.Equals(item.Name, CurrentImageClass, StringComparison.OrdinalIgnoreCase))
                 ?? LabelOptions.FirstOrDefault();
        NotifyCurrentImageClassChanged();
    }

    public async Task EnsureProjectLabelPersistedAsync(string name, string color)
    {
        if (_context.Gallery is null || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var trimmedName = name.Trim();
        if (LabelOptions.Any(item => string.Equals(item.Name, trimmedName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            var label = await _labels.AddAsync(_context.Gallery.ProjectId, trimmedName, color);
            var row = LabelOptionRow.FromOption(label, LabelOptions.Count);
            if (!LabelOptions.Any(item => string.Equals(item.Name, row.Name, StringComparison.OrdinalIgnoreCase)))
            {
                LabelOptions.Add(row);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist project label {LabelName}", trimmedName);
        }
    }

    public Task SelectLabelByIndexAsync(int labelIndex)
    {
        if (!CanManageLabels || labelIndex < 0 || labelIndex >= LabelOptions.Count)
        {
            return Task.CompletedTask;
        }

        return SelectLabelAsync(LabelOptions[labelIndex]);
    }

    partial void OnSelectedLabelChanged(LabelOptionRow? value)
    {
        OnPropertyChanged(nameof(ActiveLabelName));
        OnPropertyChanged(nameof(ActiveLabelColor));
        OnPropertyChanged(nameof(ActiveLabelId));
    }

    private bool CanManageLabel() => CanManageLabels && _context.IsNotBusy;

    public void NotifyProjectWorkflowChanged()
    {
        OnPropertyChanged(nameof(CanManageLabels));
        SelectLabelCommand.NotifyCanExecuteChanged();
        DeleteLabelCommand.NotifyCanExecuteChanged();
        ShowAddLabelDialogCommand.NotifyCanExecuteChanged();
        ShowEditLabelColorDialogCommand.NotifyCanExecuteChanged();
    }

    private void ReplaceLabelOption(LabelOptionRow original, ProjectLabelOption updated)
    {
        var index = LabelOptions.ToList().FindIndex(item => item.Id == original.Id);
        if (index < 0)
        {
            index = LabelOptions.ToList().FindIndex(item =>
                string.Equals(item.Name, original.Name, StringComparison.OrdinalIgnoreCase));
        }

        if (index < 0)
        {
            return;
        }

        var hotkey = LabelOptions[index].HotkeyText;
        var row = new LabelOptionRow(updated.Id, updated.Name, updated.Color, hotkey);
        LabelOptions[index] = row;

        if (SelectedLabel is not null
            && (SelectedLabel.Id == original.Id
                || string.Equals(SelectedLabel.Name, original.Name, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedLabel = row;
        }
        else
        {
            OnPropertyChanged(nameof(ActiveLabelColor));
        }

        NotifyCurrentImageClassChanged();
    }

    private void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(LabelingEditorViewModel.ImageClass))
        {
            return;
        }

        NotifyCurrentImageClassChanged();
    }

    private void NotifyCurrentImageClassChanged()
    {
        OnPropertyChanged(nameof(CurrentImageClass));
        OnPropertyChanged(nameof(HasCurrentImageClass));
        OnPropertyChanged(nameof(CurrentImageClassDisplay));
        OnPropertyChanged(nameof(CurrentImageClassColor));

        if (!HasCurrentImageClass)
        {
            return;
        }

        var match = LabelOptions.FirstOrDefault(item =>
            string.Equals(item.Name, CurrentImageClass, StringComparison.OrdinalIgnoreCase));
        if (match is not null && !ReferenceEquals(SelectedLabel, match))
        {
            SelectedLabel = match;
        }
    }

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LabelingWorkspaceContext.WorkflowKind)
            or nameof(LabelingWorkspaceContext.IsClassificationProject)
            or nameof(LabelingWorkspaceContext.IsLoading)
            or nameof(LabelingWorkspaceContext.IsSaving)
            or nameof(LabelingWorkspaceContext.IsImporting)
            or nameof(LabelingWorkspaceContext.IsInferring))
        {
            NotifyProjectWorkflowChanged();
        }
    }

    private void SetDialogError(string message)
    {
        DialogErrorMessage = message;
        OnPropertyChanged(nameof(HasDialogErrorMessage));
    }

    private async Task ReloadCurrentSampleAnnotationsAsync()
    {
        if (_context.Gallery is null || _context.ActiveSampleId is null)
        {
            return;
        }

        await _editor.OpenSampleAsync(_context.ActiveSampleId.Value);
    }
}
