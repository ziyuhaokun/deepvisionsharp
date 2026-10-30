using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

/// <summary>Shared state for the labeling workspace (gallery, image, busy flags).</summary>
public sealed partial class LabelingWorkspaceContext : ObservableObject
{
    private readonly IImageSourceFactory _imageSources;

    public LabelingWorkspaceContext(IImageSourceFactory imageSources)
    {
        _imageSources = imageSources;
    }

    [ObservableProperty]
    private ProjectGallery? _gallery;

    [ObservableProperty]
    private Project? _project;

    [ObservableProperty]
    private ProjectWorkflowKind _workflowKind = ProjectWorkflowKind.Classification;

    public ProjectCapabilityProfile ProjectProfile { get; private set; } =
        ProjectCapabilityCatalog.Resolve(ProjectWorkflowKind.Classification);

    public bool IsClassificationProject => ProjectWorkflow.IsClassification(WorkflowKind);

    public bool IsDetectionProject => ProjectWorkflow.IsDetection(WorkflowKind);

    [ObservableProperty]
    private string _projectName = "标记";

    [ObservableProperty]
    private string _galleryName = "未选择图库";

    [ObservableProperty]
    private string _gallerySummary = "请从图库页面进入一个图库。";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private bool _isInferring;

    [ObservableProperty]
    private Guid? _activeSampleId;

    [ObservableProperty]
    private string _activeFileName = "未选择样本";

    [ObservableProperty]
    private string _activeImagePath = string.Empty;

    [ObservableProperty]
    private ImageSource? _imageSource;

    [ObservableProperty]
    private double _imageWidth;

    [ObservableProperty]
    private double _imageHeight;

    [ObservableProperty]
    private int _sampleCount;

    [ObservableProperty]
    private int _annotatedCount;

    public bool IsNotBusy =>
        !IsLoading && !IsSaving && !IsImporting && !IsInferring;

    public bool HasImage => ImageSource is not null;

    public string ImageSizeText => ImageWidth > 0 && ImageHeight > 0
        ? $"{ImageWidth:0} x {ImageHeight:0}"
        : "—";

    public string GalleryProgressText => SampleCount > 0
        ? $"已标注 {AnnotatedCount} / {SampleCount}"
        : "暂无样本";

    partial void OnIsLoadingChanged(bool value) => NotifyBusyState();

    partial void OnIsSavingChanged(bool value) => NotifyBusyState();

    partial void OnIsImportingChanged(bool value) => NotifyBusyState();

    partial void OnIsInferringChanged(bool value) => NotifyBusyState();

    partial void OnImageSourceChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(HasImage));
    }

    partial void OnImageWidthChanged(double value) => OnPropertyChanged(nameof(ImageSizeText));

    partial void OnImageHeightChanged(double value) => OnPropertyChanged(nameof(ImageSizeText));

    partial void OnSampleCountChanged(int value) => OnPropertyChanged(nameof(GalleryProgressText));

    partial void OnAnnotatedCountChanged(int value) => OnPropertyChanged(nameof(GalleryProgressText));

    partial void OnWorkflowKindChanged(ProjectWorkflowKind value)
    {
        ProjectProfile = ProjectCapabilityCatalog.Resolve(value);
        OnPropertyChanged(nameof(ProjectProfile));
        NotifyWorkflowCapabilities();
    }

    public void SetProjectWorkflow(Project? workspaceProject, Project? selectedProject)
    {
        WorkflowKind = LabelingProjectWorkflow.ResolveKind(workspaceProject, selectedProject);
    }

    private void NotifyWorkflowCapabilities()
    {
        OnPropertyChanged(nameof(IsClassificationProject));
        OnPropertyChanged(nameof(IsDetectionProject));
    }

    private void NotifyBusyState()
    {
        OnPropertyChanged(nameof(IsNotBusy));
    }

    /// <summary>
    /// Decodes and freezes the image off the UI thread <b>without</b> publishing anything to this
    /// context. The caller commits the decoded image together with the rest of the sample state
    /// (identity, dimensions, image class) in one synchronous step, so a cancelled open can never
    /// leave the editor showing one sample while still saving against another.
    /// </summary>
    public async Task<LoadedSampleImage?> LoadImageAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        // Decode/freeze BitmapImage off the UI thread; freezable images can cross threads after Freeze.
        ImageSource? image = null;
        var width = 0;
        var height = 0;
        await Task.Run(
            () =>
            {
                if (_imageSources.TryLoadFromFile(imagePath, out var loaded, out var pixelWidth, out var pixelHeight))
                {
                    image = loaded;
                    width = pixelWidth;
                    height = pixelHeight;
                }
            },
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return image is null ? null : new LoadedSampleImage(image, width, height);
    }
}

/// <summary>Decoded image and its pixel size, not yet published to <see cref="LabelingWorkspaceContext"/>.</summary>
public sealed record LoadedSampleImage(ImageSource Image, double PixelWidth, double PixelHeight);
