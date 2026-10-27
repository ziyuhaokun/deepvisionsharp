using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Labeling;

namespace DeepVisionSharp.Shell.ViewModels.Inference;

public sealed partial class InferenceWorklistViewModel : ObservableObject
{
    private const int GalleryFetchPageSize = 200;

    /// <summary>Bumped by every new load and by <see cref="ClearAllImages"/>; a paged append stops when it changes.</summary>
    private int _loadGeneration;

    private readonly IMediator _mediator;
    private readonly ILocalFileDialogService _fileDialog;
    private readonly IProjectFileStore _fileStore;
    private readonly IImageSourceFactory _imageSources;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<InferenceWorklistViewModel> _logger;

    public InferenceWorklistViewModel(
        IMediator mediator,
        ILocalFileDialogService fileDialog,
        IProjectFileStore fileStore,
        IImageSourceFactory imageSources,
        WorkspaceSessionService workspaceSession,
        ShellSnackbarNotifier snackbar,
        ILogger<InferenceWorklistViewModel> logger)
    {
        _mediator = mediator;
        _fileDialog = fileDialog;
        _fileStore = fileStore;
        _imageSources = imageSources;
        _workspaceSession = workspaceSession;
        _snackbar = snackbar;
        _logger = logger;

        Images.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(ImageCount));
            OnPropertyChanged(nameof(ImageSourceSummary));
        };
    }

    public ObservableCollection<InferenceImageItem> Images { get; } = new();

    public ObservableCollection<InferenceGalleryOption> Galleries { get; } = new();

    public int ImageCount => Images.Count;

    [ObservableProperty]
    private bool _hasProjectContext;

    [ObservableProperty]
    private string _projectTitle = "推理";

    [ObservableProperty]
    private string _statusText = "请先在「项目」中选择项目。";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private InferenceImageItem? _selectedImage;

    [ObservableProperty]
    private InferenceImageSourceMode _sourceMode = InferenceImageSourceMode.ExternalFiles;

    [ObservableProperty]
    private InferenceGalleryOption? _selectedGallery;

    public bool IsExternalSource => SourceMode == InferenceImageSourceMode.ExternalFiles;

    public bool IsGallerySource => SourceMode == InferenceImageSourceMode.GallerySamples;

    partial void OnSourceModeChanged(InferenceImageSourceMode value)
    {
        OnPropertyChanged(nameof(IsExternalSource));
        OnPropertyChanged(nameof(IsGallerySource));
    }

    public string ImageSourceSummary
    {
        get
        {
            if (!HasProjectContext)
            {
                return "请先选择项目";
            }

            if (Images.Count == 0)
            {
                return string.Empty;
            }

            var external = Images.Count(item => item.SourceMode == InferenceImageSourceMode.ExternalFiles);
            var galleryItems = Images
                .Where(item => item.SourceMode == InferenceImageSourceMode.GallerySamples)
                .ToList();
            var galleryNames = galleryItems
                .Select(item => item.GalleryName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var parts = new List<string>();
            if (external > 0)
            {
                parts.Add($"外部 {external}");
            }

            if (galleryItems.Count > 0)
            {
                var galleryLabel = galleryNames.Count switch
                {
                    0 => $"图库 {galleryItems.Count}",
                    1 => $"图库「{galleryNames[0]}」{galleryItems.Count}",
                    _ => $"{galleryNames.Count} 个图库 {galleryItems.Count}"
                };
                parts.Add(galleryLabel);
            }

            return $"合计 {Images.Count} 张 · " + string.Join(" · ", parts);
        }
    }

    [RelayCommand]
    public void SetExpectedOk(InferenceImageItem? item)
    {
        item ??= SelectedImage;
        if (item is null) return;
        item.ExpectedClass = AnnotationClassLabels.Ok;
    }

    [RelayCommand]
    public void SetExpectedNg(InferenceImageItem? item)
    {
        item ??= SelectedImage;
        if (item is null) return;
        item.ExpectedClass = AnnotationClassLabels.Ng;
    }

    [RelayCommand]
    public void ClearExpected(InferenceImageItem? item)
    {
        item ??= SelectedImage;
        if (item is null) return;
        item.ExpectedClass = null;
    }

    [RelayCommand]
    public void ClearExternalImages()
    {
        var keep = Images
            .Where(item => item.SourceMode != InferenceImageSourceMode.ExternalFiles)
            .ToList();
        Images.Clear();
        foreach (var item in keep)
        {
            Images.Add(item);
        }

        if (SelectedImage?.SourceMode == InferenceImageSourceMode.ExternalFiles)
        {
            SelectedImage = Images.FirstOrDefault();
        }

        OnPropertyChanged(nameof(ImageSourceSummary));
    }

    [RelayCommand]
    public void ClearGalleryImages()
    {
        var keep = Images
            .Where(item => item.SourceMode != InferenceImageSourceMode.GallerySamples)
            .ToList();
        Images.Clear();
        foreach (var item in keep)
        {
            Images.Add(item);
        }

        if (SelectedImage?.SourceMode == InferenceImageSourceMode.GallerySamples)
        {
            SelectedImage = Images.FirstOrDefault();
        }

        OnPropertyChanged(nameof(ImageSourceSummary));
    }

    [RelayCommand]
    public void ClearAllImages()
    {
        // Bumps the generation so an in-flight append stops adding pages to the list the user just
        // emptied, and so its trailing "select the last added image" does not override the choice.
        _loadGeneration++;
        Images.Clear();
        SelectedImage = null;
        OnPropertyChanged(nameof(ImageSourceSummary));
    }

    public async Task ImportExternalFilesAsync()
    {
        var files = await _fileDialog.SelectFilesAsync(
            "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*").ConfigureAwait(true);
        if (files is null || files.Length == 0) return;

        SourceMode = InferenceImageSourceMode.ExternalFiles;
        var existingPaths = new HashSet<string>(Images.Select(item => item.ImagePath), StringComparer.OrdinalIgnoreCase);
        InferenceImageItem? lastAdded = null;
        var added = 0;

        foreach (var file in files.Where(File.Exists))
        {
            var fullPath = Path.GetFullPath(file);
            if (!existingPaths.Add(fullPath)) continue;

            var item = new InferenceImageItem
            {
                ImagePath = fullPath,
                DisplayName = Path.GetFileName(file),
                SourceMode = InferenceImageSourceMode.ExternalFiles,
                Preview = _imageSources.FromFile(fullPath)
            };
            Images.Add(item);
            lastAdded = item;
            added++;
        }

        if (lastAdded is not null)
        {
            SelectedImage = lastAdded;
        }

        StatusText = $"已从外部导入 {added} 张图片（列表合计 {Images.Count} 张）。";
        OnPropertyChanged(nameof(ImageSourceSummary));
    }

    public async Task AppendSelectedGalleryAsync()
    {
        var project = _workspaceSession.Current.Project;
        if (project is null || SelectedGallery is null) return;

        // A paged append runs for as long as the gallery has pages. Remember which load this is so a
        // clear (or a newer load) can stop it instead of having it refill the list.
        var generation = ++_loadGeneration;

        try
        {
            IsLoading = true;
            SourceMode = InferenceImageSourceMode.GallerySamples;

            var existingPaths = new HashSet<string>(Images.Select(item => item.ImagePath), StringComparer.OrdinalIgnoreCase);
            var existingSampleKeys = new HashSet<string>(
                Images
                    .Where(item => item.GalleryId is not null && item.SampleId is not null)
                    .Select(item => $"{item.GalleryId:N}:{item.SampleId:N}"),
                StringComparer.OrdinalIgnoreCase);

            InferenceImageItem? lastAdded = null;
            var added = 0;
            var skipped = 0;
            var expectedCount = 0;
            var missing = 0;
            var galleryId = SelectedGallery.Id;
            var galleryName = SelectedGallery.Name;
            var skip = 0;
            var totalCount = 0;

            while (true)
            {
                if (generation != _loadGeneration)
                {
                    return;
                }

                var pageResult = await _mediator.Send(
                        new ListGallerySamplesQuery(galleryId, skip, GalleryFetchPageSize))
                    .ConfigureAwait(true);

                if (generation != _loadGeneration)
                {
                    return;
                }

                if (pageResult.IsFailure || pageResult.Value is null)
                {
                    _snackbar.ShowError(pageResult.Error ?? "加载图库样本失败。", "推理");
                    return;
                }

                var page = pageResult.Value;
                totalCount = page.TotalCount;
                if (page.Items.Count == 0) break;

                var pageFileNames = page.Items.Select(s => s.FileName).ToList();
                var expectedMapResult = await _mediator.Send(
                        new GetGallerySampleImageClassesQuery(galleryId, pageFileNames))
                    .ConfigureAwait(true);
                var expectedMap = expectedMapResult.IsSuccess && expectedMapResult.Value is not null
                    ? expectedMapResult.Value
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var sample in page.Items)
                {
                    var path = _fileStore.ResolveSampleImagePath(project.Id, galleryId, sample.FileName);
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    {
                        missing++;
                        continue;
                    }

                    var fullPath = Path.GetFullPath(path);
                    var sampleKey = $"{galleryId:N}:{sample.Id:N}";
                    if (!existingPaths.Add(fullPath) || !existingSampleKeys.Add(sampleKey))
                    {
                        skipped++;
                        continue;
                    }

                    var expected = expectedMap.TryGetValue(sample.FileName, out var rawClass) && !string.IsNullOrWhiteSpace(rawClass)
                        ? rawClass.Trim()
                        : null;
                    if (expected is not null) expectedCount++;

                    var item = new InferenceImageItem
                    {
                        ImagePath = fullPath,
                        DisplayName = sample.FileName,
                        SourceMode = InferenceImageSourceMode.GallerySamples,
                        SampleId = sample.Id,
                        GalleryId = galleryId,
                        GalleryName = galleryName,
                        Preview = _imageSources.FromFile(fullPath),
                        ExpectedClass = expected
                    };
                    Images.Add(item);
                    lastAdded = item;
                    added++;
                }

                skip += page.Items.Count;
                if (skip >= page.TotalCount) break;
            }

            // The user may have cleared the list or started another load while pages were arriving;
            // selecting then would override their choice.
            if (generation != _loadGeneration)
            {
                return;
            }

            if (lastAdded is not null)
            {
                SelectedImage = lastAdded;
            }

            StatusText = added == 0 && skipped > 0
                ? $"图库「{galleryName}」无新增（共 {totalCount} 张，{skipped} 张已在列表中）"
                : added == 0 && missing > 0
                    ? $"图库「{galleryName}」无可追加样本（共 {totalCount} 张，缺失文件 {missing}）"
                    : $"已追加图库「{galleryName}」全部可加载样本 {added} 张（图库 {totalCount}，预计 {expectedCount}，跳过重复 {skipped}，缺失 {missing}，列表合计 {Images.Count}）";
            OnPropertyChanged(nameof(ImageSourceSummary));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Load gallery samples failed");
            _snackbar.ShowError(ex.Message, "推理");
        }
        finally
        {
            IsLoading = false;
        }
    }
}
