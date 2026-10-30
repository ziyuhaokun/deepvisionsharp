using System.Collections.ObjectModel;
using System.Windows;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Labeling;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

/// <summary>
/// Labeling editor. Classification samples carry one image class; detection samples carry a list
/// of boxes plus a review mark so an empty list can mean "no objects" instead of "not annotated".
/// </summary>
public sealed partial class LabelingEditorViewModel : ObservableObject,
    ILabelingSamplePersistenceHost
{
    private readonly LabelingWorkspaceContext _context;
    private readonly LabelingSamplePersistence _persistence;
    private readonly List<IReadOnlyList<AnnotationShape>> _undo = new();
    private readonly List<IReadOnlyList<AnnotationShape>> _redo = new();
    private bool _restoringHistory;

    public event EventHandler? DebouncedSaveCompleted;

    public LabelingEditorViewModel(
        LabelingWorkspaceContext context,
        IAnnotationWorkflowService annotations,
        ShellSnackbarNotifier snackbar,
        ILogger<LabelingEditorViewModel> logger,
        IMediator mediator,
        IUiDispatcher uiDispatcher,
        Func<Task> refreshGalleryAsync)
    {
        _context = context;
        _persistence = new LabelingSamplePersistence(
            this,
            annotations,
            mediator,
            snackbar,
            logger,
            uiDispatcher,
            refreshGalleryAsync);

        _context.PropertyChanged += OnContextPropertyChanged;
        Boxes.CollectionChanged += (_, _) => RenumberBoxes();
    }

    LabelingWorkspaceContext ILabelingSamplePersistenceHost.Context => _context;

    IReadOnlyList<AnnotationShape> ILabelingSamplePersistenceHost.Shapes =>
        Boxes.Select(box => box.ToShape()).ToArray();

    void ILabelingSamplePersistenceHost.ApplySampleState(
        Guid sampleId,
        string fileName,
        string imagePath,
        LoadedSampleImage image,
        double imageWidth,
        double imageHeight,
        string? imageClass,
        IReadOnlyList<AnnotationShape> shapes,
        bool isReviewed)
    {
        // One synchronous commit: identity, class and boxes always describe the same sample.
        _context.ActiveSampleId = sampleId;
        _context.ActiveFileName = fileName;
        _context.ActiveImagePath = imagePath;
        _context.ImageSource = image.Image;
        _context.ImageWidth = imageWidth > 0 ? imageWidth : image.PixelWidth;
        _context.ImageHeight = imageHeight > 0 ? imageHeight : image.PixelHeight;
        ImageClass = imageClass;

        _restoringHistory = true;
        try
        {
            Boxes.Clear();
            foreach (var shape in shapes)
            {
                Boxes.Add(BoxShapeViewModel.FromShape(shape));
            }
        }
        finally
        {
            _restoringHistory = false;
        }

        SelectedBox = null;
        IsReviewed = isReviewed;
        ZoomScale = 1;
        ViewportRect = Rect.Empty;
        if (string.IsNullOrWhiteSpace(ActiveBoxLabel) && shapes.Count > 0)
        {
            ActiveBoxLabel = shapes[0].Label;
        }

        _undo.Clear();
        _redo.Clear();
        NotifyHistoryChanged();
        NotifyBoxState();
    }

    void ILabelingSamplePersistenceHost.RaiseDebouncedSaveCompleted() =>
        DebouncedSaveCompleted?.Invoke(this, EventArgs.Empty);

    [ObservableProperty]
    private string? _imageClass;

    [ObservableProperty]
    private BoxShapeViewModel? _selectedBox;

    [ObservableProperty]
    private string? _activeBoxLabel;

    [ObservableProperty]
    private bool _isReviewed;

    [ObservableProperty]
    private BoxToolMode _activeToolMode = BoxToolMode.Draw;

    [ObservableProperty]
    private bool _showLabelNames = true;

    [ObservableProperty]
    private bool _showBoxNumbers = true;

    [ObservableProperty]
    private double _zoomScale = 1;

    /// <summary>Source-image rectangle visible in the canvas; the bird's-eye view draws it.</summary>
    [ObservableProperty]
    private Rect _viewportRect;

    public ObservableCollection<BoxShapeViewModel> Boxes { get; } = new();

    public bool IsSelectTool => ActiveToolMode == BoxToolMode.Select;

    public bool IsRectangleTool => ActiveToolMode == BoxToolMode.Draw;

    public string ZoomText => $"{ZoomScale * 100:0}%";

    public bool IsDetection => _context.IsDetectionProject;

    public bool IsBoxEditingEnabled => IsDetection && IsNotBusy;

    public bool IsEditorReadOnly => !IsNotBusy;

    public bool HasBoxes => Boxes.Count > 0;

    public int BoxCount => Boxes.Count;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public bool ShowsDetectionTools => IsDetection;

    public string ToolModeText => ActiveToolMode == BoxToolMode.Draw ? "画框" : "选择";

    public string DetectionStatusText => !IsDetection
        ? string.Empty
        : IsReviewed
            ? (Boxes.Count == 0 ? "已标记：无目标" : $"已标注 {Boxes.Count} 个目标")
            : "未标注";

    public string ImageClassSummary => string.IsNullOrWhiteSpace(ImageClass)
        ? "未分类"
        : $"图像分类：{ImageClass}";

    public bool HasImageClass => !string.IsNullOrWhiteSpace(ImageClass);

    public bool CanUseEditor => IsNotBusy;

    public bool IsNotBusy => _context.IsNotBusy;

    public bool HasImage => _context.HasImage;

    public Task OpenSampleAsync(Guid sampleId) => _persistence.OpenSampleAsync(sampleId);

    /// <summary>
    /// Cancels debounced save and waits for any in-flight save to finish after writing the current sample.
    /// </summary>
    public Task FlushPendingSaveAsync() => _persistence.FlushPendingSaveAsync();

    public void ResetEditorState()
    {
        _persistence.CancelPendingOpen();
        _persistence.CancelPendingSave();
        _context.ActiveSampleId = null;
        _context.ActiveFileName = "未选择样本";
        _context.ActiveImagePath = string.Empty;
        _context.ImageSource = null;
        _context.ImageWidth = 0;
        _context.ImageHeight = 0;
        ImageClass = null;
        _restoringHistory = true;
        try
        {
            Boxes.Clear();
        }
        finally
        {
            _restoringHistory = false;
        }

        SelectedBox = null;
        IsReviewed = false;
        ZoomScale = 1;
        ViewportRect = Rect.Empty;
        _undo.Clear();
        _redo.Clear();
        NotifyHistoryChanged();
        NotifyBoxState();
    }

    public void CancelPendingSave() => _persistence.CancelPendingSave();

    public void CancelPendingOpen() => _persistence.CancelPendingOpen();

    [RelayCommand(CanExecute = nameof(CanUseEditor))]
    private async Task SaveAsync()
    {
        _persistence.CancelPendingSave();
        await _persistence.SaveCurrentSampleAsync(refreshGallery: true, notifyUser: true);
    }

    public Task<bool> SaveCurrentSampleAsync(bool refreshGallery, bool notifyUser = false) =>
        _persistence.SaveCurrentSampleAsync(refreshGallery, notifyUser);

    public Task RequestSaveAsync() => _persistence.RequestSaveAsync();

    /// <summary>
    /// Immediately apply image class and flush save (no debounce/snackbar). Used by label selection and inference write-back.
    /// </summary>
    public async Task<bool> ApplyImageClassAndSaveAsync(string imageClass)
    {
        if (_context.ActiveSampleId is null || string.IsNullOrWhiteSpace(_context.ActiveFileName))
        {
            return false;
        }

        ImageClass = imageClass;
        _persistence.CancelPendingSave();
        return await _persistence.SaveCurrentSampleAsync(refreshGallery: false, notifyUser: false);
    }

    /// <summary>Detection: label click chooses the class for new boxes and relabels the selected box.</summary>
    public void SetActiveBoxLabel(string label)
    {
        ActiveBoxLabel = label;
        if (SelectedBox is null)
        {
            return;
        }

        BeginBoxEdit();
        SelectedBox.Label = label;
        NotifyBoxesEditedCore();
    }

    /// <summary>Called by the canvas before it mutates the box collection.</summary>
    [RelayCommand]
    private void BeginBoxEdit()
    {
        if (_restoringHistory || !IsDetection)
        {
            return;
        }

        _undo.Add(SnapshotBoxes());
        if (_undo.Count > LabelingConstants.MaxHistoryEntries)
        {
            _undo.RemoveAt(0);
        }

        _redo.Clear();
        NotifyHistoryChanged();
    }

    /// <summary>Called by the canvas after a box was added, moved, resized or deleted.</summary>
    [RelayCommand]
    private void NotifyBoxesEdited()
    {
        if (_restoringHistory)
        {
            return;
        }

        IsReviewed = true;
        NotifyBoxesEditedCore();
    }

    private void NotifyBoxesEditedCore()
    {
        NotifyBoxState();
        _ = _persistence.RequestSaveAsync();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void UndoBoxEdit()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        _redo.Add(SnapshotBoxes());
        var state = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        RestoreBoxes(state);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void RedoBoxEdit()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        _undo.Add(SnapshotBoxes());
        var state = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        RestoreBoxes(state);
    }

    [RelayCommand]
    private void SelectBox(BoxShapeViewModel? box) => SelectedBox = box;

    [RelayCommand]
    private void DeleteBox(BoxShapeViewModel? box)
    {
        if (box is null)
        {
            return;
        }

        BeginBoxEdit();
        Boxes.Remove(box);
        if (ReferenceEquals(SelectedBox, box))
        {
            SelectedBox = null;
        }

        NotifyBoxesEditedCore();
    }

    [RelayCommand]
    private void DeleteSelectedBox()
    {
        if (SelectedBox is null)
        {
            return;
        }

        BeginBoxEdit();
        Boxes.Remove(SelectedBox);
        SelectedBox = null;
        NotifyBoxesEditedCore();
    }

    /// <summary>Clears every box and records the sample as an explicit negative.</summary>
    [RelayCommand]
    private void MarkNoObjects()
    {
        BeginBoxEdit();
        Boxes.Clear();
        SelectedBox = null;
        IsReviewed = true;
        NotifyBoxesEditedCore();
    }

    /// <summary>Marks the sample as inspected while keeping its boxes.</summary>
    [RelayCommand]
    private void MarkReviewed()
    {
        IsReviewed = true;
        NotifyBoxState();
        _ = _persistence.SaveCurrentSampleAsync(refreshGallery: false);
    }

    [RelayCommand]
    private void ToggleToolMode()
    {
        ActiveToolMode = ActiveToolMode == BoxToolMode.Draw ? BoxToolMode.Select : BoxToolMode.Draw;
    }

    [RelayCommand]
    private void SetSelectTool() => ActiveToolMode = BoxToolMode.Select;

    [RelayCommand]
    private void SetRectangleTool() => ActiveToolMode = BoxToolMode.Draw;

    /// <summary>Removes every box while keeping the sample marked as reviewed.</summary>
    [RelayCommand]
    private void ClearBoxes()
    {
        if (Boxes.Count == 0)
        {
            return;
        }

        BeginBoxEdit();
        Boxes.Clear();
        SelectedBox = null;
        IsReviewed = true;
        NotifyBoxesEditedCore();
    }

    partial void OnImageClassChanged(string? value)
    {
        OnPropertyChanged(nameof(HasImageClass));
        OnPropertyChanged(nameof(ImageClassSummary));
    }

    partial void OnSelectedBoxChanged(BoxShapeViewModel? value)
    {
        foreach (var box in Boxes)
        {
            box.IsSelected = ReferenceEquals(box, value);
        }

        OnPropertyChanged(nameof(HasSelectedBox));
    }

    partial void OnActiveToolModeChanged(BoxToolMode value)
    {
        OnPropertyChanged(nameof(ToolModeText));
        OnPropertyChanged(nameof(IsSelectTool));
        OnPropertyChanged(nameof(IsRectangleTool));
    }

    partial void OnZoomScaleChanged(double value) => OnPropertyChanged(nameof(ZoomText));

    public bool HasSelectedBox => SelectedBox is not null;

    private void RenumberBoxes()
    {
        for (var index = 0; index < Boxes.Count; index++)
        {
            Boxes[index].Number = index + 1;
        }
    }

    public void NotifyProjectWorkflowChanged()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanUseEditor));
        OnPropertyChanged(nameof(IsDetection));
        OnPropertyChanged(nameof(IsBoxEditingEnabled));
        OnPropertyChanged(nameof(IsEditorReadOnly));
        OnPropertyChanged(nameof(ShowsDetectionTools));
        OnPropertyChanged(nameof(DetectionStatusText));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private IReadOnlyList<AnnotationShape> SnapshotBoxes() =>
        Boxes.Select(box => box.ToShape()).ToArray();

    private void RestoreBoxes(IReadOnlyList<AnnotationShape> shapes)
    {
        _restoringHistory = true;
        try
        {
            Boxes.Clear();
            foreach (var shape in shapes)
            {
                Boxes.Add(BoxShapeViewModel.FromShape(shape));
            }
        }
        finally
        {
            _restoringHistory = false;
        }

        SelectedBox = null;
        IsReviewed = true;
        NotifyHistoryChanged();
        NotifyBoxesEditedCore();
    }

    private void NotifyHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        UndoBoxEditCommand.NotifyCanExecuteChanged();
        RedoBoxEditCommand.NotifyCanExecuteChanged();
    }

    private void NotifyBoxState()
    {
        OnPropertyChanged(nameof(HasBoxes));
        OnPropertyChanged(nameof(BoxCount));
        OnPropertyChanged(nameof(DetectionStatusText));
    }

    private void OnContextPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LabelingWorkspaceContext.IsLoading)
            or nameof(LabelingWorkspaceContext.IsSaving)
            or nameof(LabelingWorkspaceContext.IsImporting)
            or nameof(LabelingWorkspaceContext.IsInferring)
            or nameof(LabelingWorkspaceContext.ImageSource))
        {
            OnPropertyChanged(nameof(IsNotBusy));
            OnPropertyChanged(nameof(HasImage));
            OnPropertyChanged(nameof(CanUseEditor));
            OnPropertyChanged(nameof(IsBoxEditingEnabled));
            OnPropertyChanged(nameof(IsEditorReadOnly));
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
}
