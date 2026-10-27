using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Labeling;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

/// <summary>Image-class labeling editor: one class per sample, saved to the annotation sidecar.</summary>
public sealed partial class LabelingEditorViewModel : ObservableObject,
    ILabelingSamplePersistenceHost
{
    private readonly LabelingWorkspaceContext _context;
    private readonly LabelingSamplePersistence _persistence;

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
    }

    LabelingWorkspaceContext ILabelingSamplePersistenceHost.Context => _context;

    void ILabelingSamplePersistenceHost.ApplySampleState(
        Guid sampleId,
        string fileName,
        string imagePath,
        LoadedSampleImage image,
        double imageWidth,
        double imageHeight,
        string? imageClass)
    {
        // One synchronous commit: identity and class always describe the same sample.
        _context.ActiveSampleId = sampleId;
        _context.ActiveFileName = fileName;
        _context.ActiveImagePath = imagePath;
        _context.ImageSource = image.Image;
        _context.ImageWidth = imageWidth > 0 ? imageWidth : image.PixelWidth;
        _context.ImageHeight = imageHeight > 0 ? imageHeight : image.PixelHeight;
        ImageClass = imageClass;
    }

    void ILabelingSamplePersistenceHost.RaiseDebouncedSaveCompleted() =>
        DebouncedSaveCompleted?.Invoke(this, EventArgs.Empty);

    [ObservableProperty]
    private string? _imageClass;

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
    }

    public void CancelPendingSave() => _persistence.CancelPendingSave();

    public void CancelPendingOpen() => _persistence.CancelPendingOpen();

    [RelayCommand(CanExecute = nameof(CanUseEditor))]
    private async Task SaveAsync()
    {
        _persistence.CancelPendingSave();
        await _persistence.SaveCurrentSampleAsync(refreshGallery: true, notifyUser: true);
    }

    public Task SaveCurrentSampleAsync(bool refreshGallery, bool notifyUser = false) =>
        _persistence.SaveCurrentSampleAsync(refreshGallery, notifyUser);

    public Task RequestSaveAsync() => _persistence.RequestSaveAsync();

    /// <summary>
    /// Immediately apply image class and flush save (no debounce/snackbar). Used by label selection and inference write-back.
    /// </summary>
    public async Task ApplyImageClassAndSaveAsync(string imageClass)
    {
        if (_context.ActiveSampleId is null || string.IsNullOrWhiteSpace(_context.ActiveFileName))
        {
            return;
        }

        ImageClass = imageClass;
        _persistence.CancelPendingSave();
        await _persistence.SaveCurrentSampleAsync(refreshGallery: false, notifyUser: false);
    }

    partial void OnImageClassChanged(string? value)
    {
        OnPropertyChanged(nameof(HasImageClass));
        OnPropertyChanged(nameof(ImageClassSummary));
    }

    public void NotifyProjectWorkflowChanged()
    {
        OnPropertyChanged(nameof(IsNotBusy));
        OnPropertyChanged(nameof(CanUseEditor));
        SaveCommand.NotifyCanExecuteChanged();
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
            SaveCommand.NotifyCanExecuteChanged();
        }
    }
}
