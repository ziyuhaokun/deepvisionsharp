using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Windows.Input;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Labeling;
using DeepVisionSharp.Shell.Services.Labeling.WriteBack;
using DeepVisionSharp.Shell.Services.Navigation;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

/// <summary>
/// Root coordinator for the labeling page. Composes feature-specific child ViewModels.
/// </summary>
public sealed partial class LabelingViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly IAppNavigationService _navigation;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILocalFileDialogService _fileDialog;
    private readonly IYoloDatasetImportService _yoloImport;
    private readonly ILogger<LabelingViewModel> _logger;

    public LabelingViewModel(
        IMediator mediator,
        IAnnotationWorkflowService annotations,
        IGalleryTransferService galleryTransfer,
        ILocalFileDialogService fileDialog,
        IYoloDatasetImportService yoloImport,
        IProjectLabelService labels,
        WorkspaceSessionService workspaceSession,
        IAppNavigationService navigation,
        IAppDialogService appDialogService,
        ShellSnackbarNotifier snackbar,
        ISampleThumbnailService sampleThumbnails,
        IImageSourceFactory imageSources,
        IUiDispatcher uiDispatcher,
        ITaskCapabilityResolver taskCapabilities,
        LabelingInferenceWriteBackRegistry writeBackRegistry,
        ShellTaskCenterViewModel taskCenter,
        ILogger<LabelingViewModel> logger,
        ILogger<LabelingEditorViewModel> editorLogger,
        ILogger<LabelingSampleListViewModel> samplesLogger,
        ILogger<LabelingLabelsViewModel> labelsLogger,
        ILogger<LabelingInferenceViewModel> inferenceLogger)
    {
        _mediator = mediator;
        _workspaceSession = workspaceSession;
        _navigation = navigation;
        _snackbar = snackbar;
        _fileDialog = fileDialog;
        _yoloImport = yoloImport;
        _logger = logger;

        Workspace = new LabelingWorkspaceContext(imageSources);
        // 首帧即按当前会话项目解析 WorkflowKind/Rail，避免先画一帧错误 Rail 再切换导致闪烁。
        Workspace.SetProjectWorkflow(_workspaceSession.Current.Project, _workspaceSession.Current.Project);

        Editor = new LabelingEditorViewModel(
            Workspace,
            annotations,
            snackbar,
            editorLogger,
            mediator,
            uiDispatcher,
            RefreshGalleryAsync);

        Labels = new LabelingLabelsViewModel(
            Workspace,
            labels,
            Editor,
            snackbar,
            appDialogService,
            labelsLogger);

        Samples = new LabelingSampleListViewModel(
            Workspace,
            mediator,
            galleryTransfer,
            fileDialog,
            annotations,
            Editor,
            snackbar,
            appDialogService,
            samplesLogger,
            sampleThumbnails,
            imageSources,
            uiDispatcher);

        Inference = new LabelingInferenceViewModel(
            Workspace,
            mediator,
            Editor,
            Labels,
            snackbar,
            appDialogService,
            taskCapabilities,
            writeBackRegistry,
            taskCenter,
            inferenceLogger);

        Workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public LabelingWorkspaceContext Workspace { get; }

    public LabelingEditorViewModel Editor { get; }

    public LabelingSampleListViewModel Samples { get; }

    public LabelingLabelsViewModel Labels { get; }

    public LabelingInferenceViewModel Inference { get; }

    [RelayCommand]
    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Workspace.IsLoading = true;
        Editor.CancelPendingOpen();
        Editor.CancelPendingSave();
        try
        {
            if (!await ResolveGalleryAsync(cancellationToken))
            {
                return;
            }

            var sessionVersion = _workspaceSession.Current.Version;
            RefreshProjectWorkflow();
            await LoadGalleryDataAsync(sessionVersion, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Labeling page load was canceled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load labeling page");
            _snackbar.ShowError($"加载标记页失败：{ex.Message}", "标记");
        }
        finally
        {
            Workspace.IsLoading = false;
            RefreshProjectWorkflow();
        }
    }

    [RelayCommand]
    private async Task ImportYoloDatasetAsync()
    {
        var gallery = Workspace.Gallery;
        if (gallery is null)
        {
            _snackbar.ShowWarning("请先选择图库。", "导入");
            return;
        }

        if (!Workspace.IsDetectionProject)
        {
            _snackbar.ShowWarning("YOLO 数据集只能导入目标检测项目。", "导入");
            return;
        }

        var folder = await _fileDialog.SelectFolderAsync();
        if (string.IsNullOrWhiteSpace(folder))
        {
            _snackbar.ShowInfo("已取消导入 YOLO 数据集。", "导入");
            return;
        }

        Workspace.IsImporting = true;
        try
        {
            var result = await _yoloImport.ImportAsync(new YoloDatasetImportRequest(gallery.Id, folder));
            var message = $"导入 {result.Imported}/{result.Total} 张，{result.BoxCount} 个框";
            if (result.Errors.Count > 0)
            {
                _snackbar.ShowWarning($"{message}；{result.Errors[0]}", "导入");
            }
            else if (result.Succeeded)
            {
                _snackbar.ShowSuccess(message, "导入");
            }
            else
            {
                _snackbar.ShowWarning(message, "导入");
            }

            await RefreshGalleryAsync();
        }
        catch (OperationCanceledException)
        {
            _snackbar.ShowInfo("已取消导入 YOLO 数据集。", "导入");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import YOLO dataset from {Folder}", folder);
            _snackbar.ShowError($"导入 YOLO 数据集失败：{ex.Message}", "导入");
        }
        finally
        {
            Workspace.IsImporting = false;
        }
    }

    [RelayCommand]
    private async Task BackToGallery()
    {
        // Flush (not cancel) so the current sample is persisted before leave.
        // Unloaded→DeactivateAsync also flushes, but CancelPendingSave alone
        // dropped in-flight debounce and raced with navigation.
        try
        {
            await Editor.FlushPendingSaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Labeling back-to-gallery flush save failed");
        }

        _navigation.Navigate(AppRoute.Gallery);
    }

    public async Task DeactivateAsync()
    {
        LoadCommand.Cancel();
        Samples.CancelBackgroundWork();
        Editor.CancelPendingOpen();
        try
        {
            await Editor.FlushPendingSaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Labeling deactivate flush save failed");
        }
    }

    public Task SelectLabelByIndexAsync(int labelIndex) => Labels.SelectLabelByIndexAsync(labelIndex);

    public async Task<bool> TryHandleKeyboardShortcutAsync(Key key, ModifierKeys modifiers)
    {
        if (LabelingKeyboardNavigation.IsTextInputFocused())
        {
            return false;
        }

        if (modifiers == ModifierKeys.Control && key == Key.S)
        {
            await Editor.SaveCommand.ExecuteAsync(null);
            return true;
        }

        if (key == Key.Left)
        {
            await Samples.NavigatePreviousSampleCommand.ExecuteAsync(null);
            return true;
        }

        if (key == Key.Right)
        {
            await Samples.NavigateNextSampleCommand.ExecuteAsync(null);
            return true;
        }

        var labelIndex = LabelingKeyboardNavigation.GetLabelHotkeyIndex(key);
        if (labelIndex >= 0)
        {
            await SelectLabelByIndexAsync(labelIndex);
            return true;
        }

        return false;
    }

    private async Task<bool> ResolveGalleryAsync(CancellationToken cancellationToken)
    {
        var session = _workspaceSession.Current;
        Workspace.Project = session.Project;
        Workspace.Gallery = null;

        if (session.Project is null)
        {
            Workspace.ProjectName = "标记";
            Workspace.GalleryName = "未选择图库";
            Workspace.GallerySummary = "请先从项目面板进入一个项目。";
            return true;
        }

        if (session.GalleryId is null)
        {
            Workspace.ProjectName = session.Project.Name;
            Workspace.GalleryName = "未选择图库";
            Workspace.GallerySummary = "请先从图库页面进入一个图库。";
            return true;
        }

        var summaryResult = await _mediator.Send(
            new GetGallerySummaryByIdQuery(session.GalleryId.Value),
            cancellationToken);
        if (!_workspaceSession.IsCurrent(session.Version))
        {
            return false;
        }

        var gallery = summaryResult.IsSuccess ? summaryResult.Value : null;
        if (gallery is null || gallery.ProjectId != session.Project.Id)
        {
            _workspaceSession.ClearGalleryIfCurrent(session.GalleryId.Value);
            Workspace.ProjectName = session.Project.Name;
            Workspace.GalleryName = "未选择图库";
            Workspace.GallerySummary = gallery is null
                ? "当前图库不存在或已被删除，请重新选择图库。"
                : "当前图库不属于活动项目，请重新选择图库。";
            return true;
        }

        Workspace.Gallery = gallery;
        return true;
    }

    private async Task LoadGalleryDataAsync(
        long expectedSessionVersion,
        CancellationToken cancellationToken)
    {
        if (Workspace.Gallery is null)
        {
            Samples.ResetForNoGallery();
            Labels.LabelOptions.Clear();
            Editor.ResetEditorState();
            Workspace.SampleCount = 0;
            Workspace.AnnotatedCount = 0;
            // 只加载当前工作流用得到的推理子系统，异常不拉推理模型等。
            await Inference.LoadInferenceModelsAsync(cancellationToken);

            return;
        }

        Workspace.ProjectName = Workspace.Project?.Name ?? "标记";
        Workspace.GalleryName = Workspace.Gallery.Name;
        Workspace.GallerySummary = string.IsNullOrWhiteSpace(Workspace.Gallery.Description)
            ? "管理该图库的样本标注。"
            : Workspace.Gallery.Description;

        await Labels.LoadLabelsAsync(cancellationToken);
        EnsureLoadIsCurrent(expectedSessionVersion, cancellationToken);

        await Inference.LoadInferenceModelsAsync(cancellationToken);
        EnsureLoadIsCurrent(expectedSessionVersion, cancellationToken);

        await Samples.RefreshFromGalleryAsync(cancellationToken);
        EnsureLoadIsCurrent(expectedSessionVersion, cancellationToken);

        if (Workspace.SampleCount > 0)
        {
            Guid sampleId;
            var keepActive = false;
            if (Workspace.ActiveSampleId is not null)
            {
                var activeSampleResult = await _mediator.Send(
                    new GetGallerySampleQuery(Workspace.Gallery.Id, Workspace.ActiveSampleId.Value),
                    cancellationToken);
                EnsureLoadIsCurrent(expectedSessionVersion, cancellationToken);
                keepActive = activeSampleResult.IsSuccess && activeSampleResult.Value is not null;
            }

            if (keepActive)
            {
                sampleId = Workspace.ActiveSampleId!.Value;
            }
            else
            {
                sampleId = await ResolveInitialSampleIdAsync(Workspace.Gallery.Id, cancellationToken);
                EnsureLoadIsCurrent(expectedSessionVersion, cancellationToken);
                if (sampleId == Guid.Empty)
                {
                    Editor.ResetEditorState();
                    return;
                }
            }

            await Editor.OpenSampleAsync(sampleId);
            EnsureLoadIsCurrent(expectedSessionVersion, cancellationToken);
        }
        else
        {
            Editor.ResetEditorState();
        }

        await Samples.LoadAnchorStateAsync(cancellationToken);
        EnsureLoadIsCurrent(expectedSessionVersion, cancellationToken);

        if (Samples.IsSamplePanelExpanded)
        {
            Samples.RefreshSamplePageState();
        }
    }

    private async Task<Guid> ResolveInitialSampleIdAsync(Guid galleryId, CancellationToken cancellationToken)
    {
        var anchorResult = await _mediator.Send(new GetGallerySampleAnchorQuery(galleryId), cancellationToken);
        if (anchorResult.IsSuccess && !string.IsNullOrWhiteSpace(anchorResult.Value))
        {
            var anchorSampleResult = await _mediator.Send(
                new GetGallerySampleByFileNameQuery(galleryId, anchorResult.Value!),
                cancellationToken);
            if (anchorSampleResult.IsSuccess && anchorSampleResult.Value is not null)
            {
                return anchorSampleResult.Value.Id;
            }
        }

        var firstPageResult = await _mediator.Send(
            new ListGallerySamplesQuery(galleryId, 0, 1),
            cancellationToken);
        var firstSample = firstPageResult.IsSuccess ? firstPageResult.Value?.Items.FirstOrDefault() : null;
        return firstSample?.Id ?? Guid.Empty;
    }

    private void EnsureLoadIsCurrent(long expectedSessionVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_workspaceSession.IsCurrent(expectedSessionVersion))
        {
            throw new OperationCanceledException("Workspace session changed during labeling load.");
        }
    }

    private async Task RefreshGalleryAsync()
    {
        if (Workspace.Gallery is null)
        {
            return;
        }

        var galleryId = Workspace.Gallery.Id;
        var projectId = Workspace.Gallery.ProjectId;
        var sessionVersion = _workspaceSession.Current.Version;

        var summaryResult = await _mediator.Send(new GetGallerySummaryByIdQuery(galleryId));
        var session = _workspaceSession.Current;
        if (session.Version != sessionVersion
            || session.Project?.Id != projectId
            || session.GalleryId != galleryId
            || Workspace.Gallery?.Id != galleryId)
        {
            return;
        }

        if (summaryResult.IsSuccess && summaryResult.Value is not null)
        {
            Workspace.Gallery = summaryResult.Value;
        }

        await Samples.RefreshFromGalleryAsync();
    }

    private void RefreshProjectWorkflow()
    {
        Workspace.SetProjectWorkflow(Workspace.Project, _workspaceSession.Current.Project);
        NotifyWorkflowProperties();
        Editor.NotifyProjectWorkflowChanged();
        Inference.NotifyProjectWorkflowChanged();
        Labels.NotifyProjectWorkflowChanged();
    }

    private void NotifyWorkflowProperties()
    {
        OnPropertyChanged(nameof(Workspace));
    }

    private void OnWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LabelingWorkspaceContext.Project)
            or nameof(LabelingWorkspaceContext.WorkflowKind))
        {
            NotifyWorkflowProperties();
        }
    }
}
