using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using Microsoft.Extensions.Logging;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Labeling;
using DeepVisionSharp.Shell.ViewModels;

namespace DeepVisionSharp.Shell.ViewModels.Labeling;

public sealed partial class LabelingSampleListViewModel : ObservableObject,
    ILabelingSampleNavigationHost,
    ILabelingSampleImportHost,
    ILabelingSampleMigrateHost
{
    private readonly LabelingWorkspaceContext _context;
    private readonly IMediator _mediator;
    private readonly LabelingEditorViewModel _editor;
    private readonly LabelingSampleThumbnailLoader _thumbnails;
    private readonly LabelingSampleNavigation _navigation;
    private readonly LabelingSampleImport _import;
    private readonly LabelingSampleMigrateActions _migrate;

    public LabelingSampleListViewModel(
        LabelingWorkspaceContext context,
        IMediator mediator,
        IGalleryTransferService galleryTransfer,
        ILocalFileDialogService fileDialog,
        IAnnotationWorkflowService annotations,
        LabelingEditorViewModel editor,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService,
        ILogger<LabelingSampleListViewModel> logger,
        ISampleThumbnailService sampleThumbnails,
        IImageSourceFactory imageSources,
        IUiDispatcher uiDispatcher)
    {
        _context = context;
        _mediator = mediator;
        _editor = editor;
        _thumbnails = new LabelingSampleThumbnailLoader(
            sampleThumbnails,
            imageSources,
            uiDispatcher,
            logger,
            () => IsSamplePanelExpanded,
            () => SamplePageItems);
        _navigation = new LabelingSampleNavigation(
            this,
            mediator,
            annotations,
            snackbar,
            logger);
        _import = new LabelingSampleImport(
            this,
            mediator,
            galleryTransfer,
            fileDialog,
            snackbar,
            logger);
        _migrate = new LabelingSampleMigrateActions(
            this,
            mediator,
            appDialogService,
            snackbar,
            logger);

        Samples.CollectionChanged += OnSamplesChanged;
        _context.PropertyChanged += OnContextPropertyChanged;
        _editor.DebouncedSaveCompleted += OnDebouncedSaveCompleted;
    }

    LabelingWorkspaceContext ILabelingSampleNavigationHost.Context => _context;

    string? ILabelingSampleNavigationHost.ImageClass => _editor.ImageClass;

    LabelingWorkspaceContext ILabelingSampleImportHost.Context => _context;

    LabelingWorkspaceContext ILabelingSampleMigrateHost.Context => _context;

    Task ILabelingSampleNavigationHost.OpenSampleAsync(Guid sampleId) =>
        _editor.OpenSampleAsync(sampleId);

    void ILabelingSampleNavigationHost.InvalidateSampleThumbnailLoads() =>
        InvalidateSampleThumbnailLoads();

    void ILabelingSampleNavigationHost.ScheduleSampleThumbnailLoad() =>
        ScheduleSampleThumbnailLoad();

    void ILabelingSampleNavigationHost.HandleSampleRowSelectionChanged(LabelingSampleRow row) =>
        OnSampleRowSelectionChanged(row);

    void ILabelingSampleNavigationHost.NotifyActiveSamplePositionTextChanged() =>
        OnPropertyChanged(nameof(ActiveSamplePositionText));

    void ILabelingSampleNavigationHost.NotifySamplePageState() => NotifySamplePageState();

    void ILabelingSampleNavigationHost.NotifySampleCounts() => NotifySampleCounts();

    void ILabelingSampleNavigationHost.NotifyHasSamplesChanged()
    {
        OnPropertyChanged(nameof(HasSamples));
        OnPropertyChanged(nameof(HasNoSamples));
    }

    void ILabelingSampleNavigationHost.NotifyAnnotatedCountChanged() =>
        OnPropertyChanged(nameof(AnnotatedCount));

    void ILabelingSampleNavigationHost.NotifyAnchorCommandsChanged()
    {
        ClearSampleAnchorCommand.NotifyCanExecuteChanged();
        SetSampleAnchorCommand.NotifyCanExecuteChanged();
    }

    void ILabelingSampleImportHost.CancelPendingSave() => _editor.CancelPendingSave();
    Task ILabelingSampleImportHost.RefreshFromGalleryAsync(CancellationToken cancellationToken) =>
        RefreshFromGalleryAsync(cancellationToken);

    Task ILabelingSampleImportHost.SelectSampleAsync(Guid sampleId) =>
        _navigation.SelectSampleAsync(sampleId);

    Task ILabelingSampleMigrateHost.RefreshFromGalleryAsync(CancellationToken cancellationToken) =>
        RefreshFromGalleryAsync(cancellationToken);

    Task ILabelingSampleMigrateHost.SelectSampleAsync(Guid sampleId) =>
        _navigation.SelectSampleAsync(sampleId);

    Task ILabelingSampleMigrateHost.ApplyImageClassNgAndSaveAsync() =>
        _editor.ApplyImageClassAndSaveAsync(LabelingConstants.ImageClassNg);

    void ILabelingSampleMigrateHost.ResetEditorState() => _editor.ResetEditorState();

    void ILabelingSampleMigrateHost.ApplyAnchorFlagsToRows() =>
        _navigation.ApplyAnchorFlagsToRows();

    void ILabelingSampleMigrateHost.NotifyMoveSelectionStateChanged() =>
        NotifyMoveSelectionStateChanged();

    public ObservableCollection<LabelingSampleRow> Samples { get; } = new();

    public ObservableCollection<LabelingSampleRow> SamplePageItems { get; } = new();

    public GalleryReorganizeDialogState ReorganizeDialog { get; } = new();

    public HashSet<string> SelectedSampleFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int SelectedSampleCount => SelectedSampleFileNames.Count;

    /// <summary>
    /// 勾选样本时迁移勾选集合；未勾选时迁移当前打开的样本。
    /// </summary>
    public bool CanMoveSelectedSamples =>
        _context.Gallery is not null
        && _context.IsNotBusy
        && (SelectedSampleCount > 0 || _context.ActiveSampleId is not null);

    public bool CanDeleteCurrentSample =>
        _context.ActiveSampleId is not null
        && _context.Gallery is not null
        && _context.IsNotBusy;

    /// <summary>
    /// NG(3)：标注当前图为 NG 并迁移到右键配置的目标图库。
    /// </summary>
    public bool CanSetImageClassNgAndMove =>
        _context.IsClassificationProject
        && _context.Gallery is not null
        && _context.ActiveSampleId is not null
        && _context.IsNotBusy
        && _context.HasImage;

    public bool HasNgMoveTarget => NgMoveTargetGalleryId is not null;

    public string NgMoveTargetSummary => HasNgMoveTarget
        ? $"目标图库：{NgMoveTargetGalleryName}"
        : "未设置目标图库（右键选择）";

    public string NgAndMoveButtonToolTip =>
        HasNgMoveTarget
            ? $"标注 NG 并迁移到「{NgMoveTargetGalleryName}」（快捷键 3；右键重选图库）"
            : "标注 NG 并迁移到目标图库（快捷键 3；右键先选择图库）";

    [ObservableProperty]
    private Guid? _ngMoveTargetGalleryId;

    [ObservableProperty]
    private string? _ngMoveTargetGalleryName;

    [ObservableProperty]
    private string? _anchorFileName;

    [ObservableProperty]
    private bool _isSamplePanelExpanded;

    [ObservableProperty]
    private int _samplePageIndex;

    public int SampleCount => _context.SampleCount;

    public int AnnotatedCount => _context.AnnotatedCount;

    public bool HasSamples => SampleCount > 0;

    public bool HasNoSamples => SampleCount <= 0;

    public string ActiveSamplePositionText
    {
        get
        {
            var gallery = _context.Gallery;
            if (gallery is null || SampleCount == 0)
            {
                return string.Empty;
            }

            var index = _navigation.ActiveSampleIndex;
            if (index < 0)
            {
                index = 0;
            }

            return $"{index + 1} / {SampleCount}";
        }
    }

    public int SamplePageCount => SampleCount <= 0
        ? 0
        : (int)Math.Ceiling(SampleCount / (double)LabelingConstants.SamplePageSize);

    public string SamplePageText => SamplePageCount == 0 ? "0 / 0" : $"{SamplePageIndex + 1} / {SamplePageCount}";

    public bool CanGoToPreviousSamplePage => SamplePageIndex > 0;

    public bool CanGoToNextSamplePage => SamplePageIndex < SamplePageCount - 1;

    public string SamplePanelToggleTooltip => IsSamplePanelExpanded ? "收起样本列表" : "展开样本列表";

    private bool CanUseEditor => _editor.CanUseEditor;

    public bool CanImportImages => _context.IsNotBusy && _context.Gallery is not null;

    [RelayCommand(CanExecute = nameof(CanUseEditor))]
    private Task SelectSampleAsync(Guid sampleId) => _navigation.SelectSampleAsync(sampleId);

    [RelayCommand(CanExecute = nameof(CanImportImages))]
    private Task ImportImagesAsync() => _import.ImportImagesAsync();

    [RelayCommand(CanExecute = nameof(CanUseEditor))]
    private Task NavigatePreviousSampleAsync() => _navigation.NavigatePreviousAsync();

    [RelayCommand(CanExecute = nameof(CanUseEditor))]
    private Task NavigateNextSampleAsync() => _navigation.NavigateNextAsync();

    [RelayCommand]
    private void ToggleSamplePanel()
    {
        IsSamplePanelExpanded = !IsSamplePanelExpanded;
    }

    [RelayCommand(CanExecute = nameof(CanGoToPreviousSamplePage))]
    private void GoToPreviousSamplePage()
    {
        if (SamplePageIndex > 0)
        {
            SamplePageIndex--;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoToNextSamplePage))]
    private void GoToNextSamplePage()
    {
        if (SamplePageIndex < SamplePageCount - 1)
        {
            SamplePageIndex++;
        }
    }

    [RelayCommand(CanExecute = nameof(CanDeleteCurrentSample))]
    private async Task ConfirmDeleteCurrentSampleAsync()
    {
        if (_context.ActiveSampleId is not Guid sampleId)
        {
            return;
        }

        await _migrate.ConfirmAndDeleteSampleAsync(sampleId);
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSampleById))]
    private Task ConfirmDeleteSampleAsync(Guid sampleId) =>
        _migrate.ConfirmAndDeleteSampleAsync(sampleId);

    [RelayCommand(CanExecute = nameof(CanSetSampleAnchor))]
    private Task SetSampleAnchorAsync(LabelingSampleRow row) => _navigation.SetSampleAnchorAsync(row);

    [RelayCommand(CanExecute = nameof(CanClearSampleAnchor))]
    private Task ClearSampleAnchorAsync() => _navigation.ClearSampleAnchorAsync();

    private bool CanSetSampleAnchor(LabelingSampleRow row) =>
        row is not null
        && _context.Gallery is not null
        && _context.IsNotBusy
        && !string.Equals(AnchorFileName, row.FileName, StringComparison.OrdinalIgnoreCase);

    private bool CanClearSampleAnchor() =>
        !string.IsNullOrWhiteSpace(AnchorFileName)
        && _context.Gallery is not null
        && _context.IsNotBusy;

    private bool CanDeleteSampleById(Guid sampleId) =>
        sampleId != Guid.Empty && _context.Gallery is not null && _context.IsNotBusy;

    partial void OnNgMoveTargetGalleryIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(HasNgMoveTarget));
        OnPropertyChanged(nameof(NgMoveTargetSummary));
        OnPropertyChanged(nameof(NgAndMoveButtonToolTip));
    }

    partial void OnNgMoveTargetGalleryNameChanged(string? value)
    {
        OnPropertyChanged(nameof(NgMoveTargetSummary));
        OnPropertyChanged(nameof(NgAndMoveButtonToolTip));
    }

    [RelayCommand]
    private Task ShowNgMoveTargetDialogAsync() => _migrate.ShowNgMoveTargetDialogAsync();

    [RelayCommand(CanExecute = nameof(CanSetImageClassNgAndMove))]
    private Task SetImageClassNgAndMoveAsync() => _migrate.SetImageClassNgAndMoveAsync();

    [RelayCommand(CanExecute = nameof(CanMoveSelectedSamples))]
    private Task ShowMoveSamplesDialogAsync() => _migrate.ShowMoveSamplesDialogAsync();

    private void NotifyMoveSelectionStateChanged()
    {
        OnPropertyChanged(nameof(SelectedSampleCount));
        OnPropertyChanged(nameof(CanMoveSelectedSamples));
        ShowMoveSamplesDialogCommand.NotifyCanExecuteChanged();
    }

    private void NotifyImportToolStateChanged()
    {
        OnPropertyChanged(nameof(CanImportImages));
        OnPropertyChanged(nameof(CanMoveSelectedSamples));
        OnPropertyChanged(nameof(CanDeleteCurrentSample));
        OnPropertyChanged(nameof(CanSetImageClassNgAndMove));
        ImportImagesCommand.NotifyCanExecuteChanged();
        ShowMoveSamplesDialogCommand.NotifyCanExecuteChanged();
        SetImageClassNgAndMoveCommand.NotifyCanExecuteChanged();
        SelectSampleCommand.NotifyCanExecuteChanged();
        NavigatePreviousSampleCommand.NotifyCanExecuteChanged();
        NavigateNextSampleCommand.NotifyCanExecuteChanged();
        ConfirmDeleteCurrentSampleCommand.NotifyCanExecuteChanged();
        ConfirmDeleteSampleCommand.NotifyCanExecuteChanged();
        SetSampleAnchorCommand.NotifyCanExecuteChanged();
        ClearSampleAnchorCommand.NotifyCanExecuteChanged();
    }

    private void OnSampleRowSelectionChanged(LabelingSampleRow row)
    {
        if (row.IsSelected)
        {
            SelectedSampleFileNames.Add(row.FileName);
        }
        else
        {
            SelectedSampleFileNames.Remove(row.FileName);
        }

        NotifyMoveSelectionStateChanged();
    }

    public async Task RefreshFromGalleryAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Gallery is null)
        {
            NotifyImportToolStateChanged();
            return;
        }

        var galleryId = _context.Gallery.Id;
        var summaryResult = await _mediator.Send(
            new GetGallerySummaryByIdQuery(galleryId),
            cancellationToken);
        if (_context.Gallery?.Id != galleryId)
        {
            return;
        }

        if (summaryResult.IsSuccess && summaryResult.Value is not null)
        {
            _context.Gallery = summaryResult.Value;
        }

        _context.SampleCount = _context.Gallery.SampleCount;
        _context.AnnotatedCount = _context.Gallery.AnnotatedCount;
        await _navigation.LoadAnchorStateAsync(cancellationToken);
        await _navigation.LoadSamplePageAsync(
            SamplePageIndex,
            refreshActiveIndex: true,
            cancellationToken: cancellationToken);
        NotifyImportToolStateChanged();
    }

    public void ResetForNoGallery()
    {
        _navigation.ResetForNoGallery();
        NotifyMoveSelectionStateChanged();
        NotifyImportToolStateChanged();
    }

    public Task LoadAnchorStateAsync(CancellationToken cancellationToken = default) =>
        _navigation.LoadAnchorStateAsync(cancellationToken);

    public void RefreshSampleRows() => _navigation.RefreshSampleRows();

    public void RefreshSamplePageState() => _navigation.RefreshSamplePageState();

    public void ScheduleSampleThumbnailLoad() => _thumbnails.Schedule();

    public void InvalidateSampleThumbnailLoads() => _thumbnails.Cancel();

    partial void OnIsSamplePanelExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(SamplePanelToggleTooltip));
        if (value)
        {
            RefreshSamplePageState();
        }
    }

    partial void OnSamplePageIndexChanged(int value) =>
        _navigation.OnSamplePageIndexChanged(value);

    private void OnContextPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LabelingWorkspaceContext.ActiveSampleId)
            or nameof(LabelingWorkspaceContext.HasImage))
        {
            if (e.PropertyName is nameof(LabelingWorkspaceContext.ActiveSampleId))
            {
                _navigation.ScheduleActiveSampleIndexUpdate();
                _navigation.UpdateSampleActiveStates();
            }

            OnPropertyChanged(nameof(CanDeleteCurrentSample));
            OnPropertyChanged(nameof(CanMoveSelectedSamples));
            OnPropertyChanged(nameof(CanSetImageClassNgAndMove));
            ConfirmDeleteCurrentSampleCommand.NotifyCanExecuteChanged();
            ShowMoveSamplesDialogCommand.NotifyCanExecuteChanged();
            SetImageClassNgAndMoveCommand.NotifyCanExecuteChanged();
        }
        else if (e.PropertyName is nameof(LabelingWorkspaceContext.SampleCount))
        {
            OnPropertyChanged(nameof(SampleCount));
            OnPropertyChanged(nameof(ActiveSamplePositionText));
            NotifySamplePageState();
        }
        else if (e.PropertyName is nameof(LabelingWorkspaceContext.AnnotatedCount))
        {
            OnPropertyChanged(nameof(AnnotatedCount));
        }
        else if (e.PropertyName is nameof(LabelingWorkspaceContext.IsLoading)
            or nameof(LabelingWorkspaceContext.IsSaving)
            or nameof(LabelingWorkspaceContext.IsImporting)
            or nameof(LabelingWorkspaceContext.IsInferring)
            or nameof(LabelingWorkspaceContext.IsNotBusy)
            or nameof(LabelingWorkspaceContext.Gallery))
        {
            if (e.PropertyName is nameof(LabelingWorkspaceContext.Gallery)
                && NgMoveTargetGalleryId is not null
                && _context.Gallery is not null
                && NgMoveTargetGalleryId == _context.Gallery.Id)
            {
                NgMoveTargetGalleryId = null;
                NgMoveTargetGalleryName = null;
            }

            NotifyImportToolStateChanged();
        }
    }

    private void OnDebouncedSaveCompleted(object? sender, EventArgs e) =>
        _navigation.UpdateActiveSampleAnnotationState();

    private void OnSamplesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSamples));
        OnPropertyChanged(nameof(HasNoSamples));
    }

    private void NotifySamplePageState()
    {
        OnPropertyChanged(nameof(SamplePageCount));
        OnPropertyChanged(nameof(SamplePageText));
        OnPropertyChanged(nameof(CanGoToPreviousSamplePage));
        OnPropertyChanged(nameof(CanGoToNextSamplePage));
        GoToPreviousSamplePageCommand.NotifyCanExecuteChanged();
        GoToNextSamplePageCommand.NotifyCanExecuteChanged();
    }

    private void NotifySampleCounts()
    {
        OnPropertyChanged(nameof(SampleCount));
        OnPropertyChanged(nameof(AnnotatedCount));
    }

    /// <summary>
    /// Cancels in-flight thumbnail decode/IO (call on page unload / sample page change).
    /// </summary>
    public void CancelBackgroundWork() => _thumbnails.Cancel();
}
