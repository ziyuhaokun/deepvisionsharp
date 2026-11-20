using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using System.Collections.ObjectModel;
using System.IO;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Ports;

namespace DeepVisionSharp.Presentation.ViewModels.Training;

/// <summary>
/// Training run preview images, classification plots, and lightbox commands.
/// Owned by <see cref="TrainingMonitorViewModel"/>.
/// </summary>
public sealed partial class TrainingPreviewPanelViewModel : ObservableObject
{
    private readonly TrainingJobsViewModel _jobs;
    private readonly TrainingPreviewPoller _previewPolling;
    private readonly IProcessLauncher _processLauncher;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IClipboardService _clipboard;
    private readonly IUiDispatcher _uiDispatcher;
    private Func<bool>? _canPollPreview;

    public TrainingPreviewPanelViewModel(
        TrainingJobsViewModel jobs,
        TrainingPreviewPoller previewPolling,
        IProcessLauncher processLauncher,
        ShellSnackbarNotifier snackbar,
        IClipboardService clipboard,
        IUiDispatcher uiDispatcher)
    {
        _jobs = jobs;
        _previewPolling = previewPolling;
        _processLauncher = processLauncher;
        _snackbar = snackbar;
        _clipboard = clipboard;
        _uiDispatcher = uiDispatcher;
    }

    [ObservableProperty]
    private IReadOnlyList<TrainingPreviewItem> _previewImages = [];

    public ObservableCollection<string> TrainingPlotImagePaths { get; } = new();

    [ObservableProperty]
    private bool _hasPreviewImages;

    [ObservableProperty]
    private string? _previewPollingError;

    [ObservableProperty]
    private string? _previewLightboxImagePath;

    public bool IsPreviewLightboxOpen => !string.IsNullOrWhiteSpace(PreviewLightboxImagePath);

    public bool HasPreviewPollingError => !string.IsNullOrWhiteSpace(PreviewPollingError);

    public bool HasTrainingPlotImages => TrainingPlotImagePaths.Count > 0;

    public bool IsSelectedJobClassificationModel =>
        _jobs.SelectedJob is not null
        && ModelTypeNames.IsClassificationModelType(_jobs.SelectedJob.ModelType);

    public bool ShowClassificationTrainingPlots =>
        IsSelectedJobClassificationModel && HasTrainingPlotImages;

    public bool ShowPreviewEmptyState =>
        !HasPreviewImages
        && _jobs.SelectedJob is not null
        && ModelTypeNames.IsKnownModelType(_jobs.SelectedJob.ModelType);

    public string PreviewEmptyTitle => "\u6682\u65e0\u9884\u89c8";

    public string PreviewEmptyHint => IsSelectedJobClassificationModel
        ? "训练过程中会定期生成最多 9 张验证集样本的独立预览。"
        : "训练过程中会按轮次生成验证集的检测预览。";

    public void AttachPollingGate(Func<bool> canPollPreview) =>
        _canPollPreview = canPollPreview;

    public void NotifyJobContextChanged()
    {
        OnPropertyChanged(nameof(IsSelectedJobClassificationModel));
        OnPropertyChanged(nameof(ShowPreviewEmptyState));
        OnPropertyChanged(nameof(PreviewEmptyTitle));
        OnPropertyChanged(nameof(PreviewEmptyHint));
        OnPropertyChanged(nameof(ShowClassificationTrainingPlots));
        OnPropertyChanged(nameof(HasTrainingPlotImages));
    }

    public void StartPolling()
    {
        PreviewPollingError = null;
        RefreshPreviewImages(force: true);

        var job = _jobs.SelectedJob;
        if (job is null || string.IsNullOrWhiteSpace(job.OutputRoot))
        {
            return;
        }

        _previewPolling.Start(
            job.Id,
            () => _canPollPreview?.Invoke() ?? false,
            () => _uiDispatcher.InvokeAsync(() =>
            {
                if (_jobs.SelectedJob?.Id == job.Id)
                {
                    RefreshPreviewImages(force: false);
                }
            }),
            ReportPollingError);
    }

    public void StopPolling() => _previewPolling.Stop();

    public bool RefreshPreviewImages(bool force)
    {
        var images = _previewPolling.GetUpdatedPreviewImages(_jobs.SelectedJob?.OutputRoot, PreviewImages, force);
        if (images is null)
        {
            return false;
        }

        PreviewImages = images;
        HasPreviewImages = images.Count > 0;
        return true;
    }

    public void RefreshTrainingPlotImages()
    {
        var paths = ClassificationResultsCsvImporter.FindPlotImagePaths(_jobs.SelectedJob?.OutputRoot);
        var changed = paths.Count != TrainingPlotImagePaths.Count
            || !paths.SequenceEqual(TrainingPlotImagePaths, StringComparer.OrdinalIgnoreCase);
        if (!changed)
        {
            OnPropertyChanged(nameof(HasTrainingPlotImages));
            OnPropertyChanged(nameof(ShowClassificationTrainingPlots));
            return;
        }

        TrainingPlotImagePaths.Clear();
        foreach (var path in paths)
        {
            TrainingPlotImagePaths.Add(path);
        }

        OnPropertyChanged(nameof(TrainingPlotImagePaths));
        OnPropertyChanged(nameof(HasTrainingPlotImages));
        OnPropertyChanged(nameof(ShowClassificationTrainingPlots));
    }

    public void ClearTrainingPlotImages()
    {
        TrainingPlotImagePaths.Clear();
        OnPropertyChanged(nameof(HasTrainingPlotImages));
        OnPropertyChanged(nameof(ShowClassificationTrainingPlots));
    }

    [RelayCommand]
    private void OpenPreviewLightbox(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return;
        }

        PreviewLightboxImagePath = imagePath;
    }

    [RelayCommand]
    private void ClosePreviewLightbox()
    {
        PreviewLightboxImagePath = null;
    }

    /// <summary>Copy a training preview image to the Windows clipboard as a bitmap.</summary>
    [RelayCommand]
    private void CopyPreviewImage(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            _snackbar.ShowWarning("预览图片不存在。", "预览");
            return;
        }

        try
        {
            _clipboard.SetImageFromFile(imagePath);
            _snackbar.ShowSuccess("预览图已复制到剪贴板。", "预览");
        }
        catch (IOException ex)
        {
            _snackbar.ShowError($"复制失败：文件正被占用或无法读取（{ex.Message}）", "预览");
        }
        catch (UnauthorizedAccessException ex)
        {
            _snackbar.ShowError($"复制失败：无权限读取文件（{ex.Message}）", "预览");
        }
        catch (Exception ex)
        {
            _snackbar.ShowError($"复制失败：{ex.Message}", "预览");
        }
    }

    [RelayCommand]
    private void OpenTrainingPlotsFolder()
    {
        var folder = ClassificationResultsCsvImporter.ResolvePlotsDirectory(_jobs.SelectedJob?.OutputRoot);
        if (string.IsNullOrWhiteSpace(folder) || !_processLauncher.TryOpenFolder(folder))
        {
            _snackbar.ShowWarning("训练图表目录不存在（训练完成后会写入 plots/）。", "训练");
        }
    }

    partial void OnPreviewLightboxImagePathChanged(string? value) =>
        OnPropertyChanged(nameof(IsPreviewLightboxOpen));

    partial void OnPreviewPollingErrorChanged(string? value) =>
        OnPropertyChanged(nameof(HasPreviewPollingError));

    partial void OnHasPreviewImagesChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPreviewEmptyState));
        OnPropertyChanged(nameof(PreviewEmptyHint));
    }

    private void ReportPollingError(Exception ex)
    {
        _ = _uiDispatcher.InvokeAsync(() =>
        {
            PreviewPollingError = $"预览轮询失败：{ex.Message}";
        });
    }
}
