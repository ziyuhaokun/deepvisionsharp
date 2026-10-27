using System.Collections.ObjectModel;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// Sample list paging, adjacent navigation, anchor state, and active-index tracking.
/// Owned by <see cref="LabelingSampleListViewModel"/>.
/// </summary>
public sealed class LabelingSampleNavigation
{
    private readonly ILabelingSampleNavigationHost _host;
    private readonly IMediator _mediator;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger _logger;
    private readonly List<LabelingSampleRow> _trackedSampleRows = [];
    private int _samplePageLoadGeneration;
    private int _activeSampleIndex = -1;

    public LabelingSampleNavigation(
        ILabelingSampleNavigationHost host,
        IMediator mediator,
        IAnnotationWorkflowService annotations,
        ShellSnackbarNotifier snackbar,
        ILogger logger)
    {
        _host = host;
        _mediator = mediator;
        _annotations = annotations;
        _snackbar = snackbar;
        _logger = logger;
    }

    public int ActiveSampleIndex => _activeSampleIndex;

    public async Task SelectSampleAsync(Guid sampleId)
    {
        await _host.OpenSampleAsync(sampleId);
        UpdateActiveSampleIndexFromCurrentPage(sampleId);
        UpdateSampleActiveStates();
        UpdateActiveSampleAnnotationState();
    }

    public Task NavigatePreviousAsync() => NavigateSampleAsync(-1);

    public Task NavigateNextAsync() => NavigateSampleAsync(1);

    public async Task SetSampleAnchorAsync(LabelingSampleRow row)
    {
        var gallery = _host.Context.Gallery;
        if (gallery is null)
        {
            return;
        }

        var result = await _mediator.Send(new SetGallerySampleAnchorCommand(gallery.Id, row.FileName));
        if (result.IsFailure)
        {
            _snackbar.ShowError(result.Error ?? "设置标注锚点失败。", "标注锚点");
            return;
        }

        _host.AnchorFileName = row.FileName;
        ApplyAnchorFlagsToRows();
        _snackbar.ShowSuccess($"已将「{row.FileName}」设为标注锚点。下次打开图库时将跳转到此样本。", "标注锚点");
    }

    public async Task ClearSampleAnchorAsync()
    {
        var gallery = _host.Context.Gallery;
        if (gallery is null)
        {
            return;
        }

        var result = await _mediator.Send(new SetGallerySampleAnchorCommand(gallery.Id, null));
        if (result.IsFailure)
        {
            _snackbar.ShowError(result.Error ?? "取消标注锚点失败。", "标注锚点");
            return;
        }

        _host.AnchorFileName = null;
        ApplyAnchorFlagsToRows();
        _snackbar.ShowSuccess("已取消标注锚点。", "标注锚点");
    }

    public void ApplyAnchorFlagsToRows()
    {
        var anchorFileName = _host.AnchorFileName;
        foreach (var row in _host.SamplePageItems)
        {
            row.IsAnchor = !string.IsNullOrWhiteSpace(anchorFileName)
                && string.Equals(row.FileName, anchorFileName, StringComparison.OrdinalIgnoreCase);
        }

        _host.NotifyAnchorCommandsChanged();
    }

    public async Task LoadAnchorStateAsync(CancellationToken cancellationToken = default)
    {
        var gallery = _host.Context.Gallery;
        if (gallery is null)
        {
            _host.AnchorFileName = null;
            ApplyAnchorFlagsToRows();
            return;
        }

        var result = await _mediator.Send(new GetGallerySampleAnchorQuery(gallery.Id), cancellationToken);
        _host.AnchorFileName = result.IsSuccess ? result.Value : null;
        ApplyAnchorFlagsToRows();
    }

    public void RefreshSampleRows()
    {
        ShellAsync.FireAndForget(
            () => LoadSamplePageAsync(_host.SamplePageIndex, refreshActiveIndex: true),
            _logger,
            "RefreshSampleRows",
            ex => _snackbar.ShowError($"刷新样本列表失败：{ex.Message}", "标注"));
    }

    public void RefreshSamplePageState()
    {
        ClampSamplePageIndex();
        _host.NotifySamplePageState();
        _host.ScheduleSampleThumbnailLoad();
    }

    public void OnSamplePageIndexChanged(int pageIndex)
    {
        ShellAsync.FireAndForget(
            () => LoadSamplePageAsync(pageIndex),
            _logger,
            "LoadSamplePage",
            ex => _snackbar.ShowError($"加载样本分页失败：{ex.Message}", "标注"));
    }

    public void ScheduleActiveSampleIndexUpdate()
    {
        ShellAsync.FireAndForget(
            UpdateActiveSampleIndexAsync,
            _logger,
            "UpdateActiveSampleIndex");
    }

    public void ResetForNoGallery()
    {
        Interlocked.Increment(ref _samplePageLoadGeneration);
        _activeSampleIndex = -1;
        DetachTrackedRows();
        _host.InvalidateSampleThumbnailLoads();
        _host.Samples.Clear();
        _host.SamplePageItems.Clear();
        _host.SelectedSampleFileNames.Clear();
        _host.AnchorFileName = null;
        _host.Context.SampleCount = 0;
        _host.Context.AnnotatedCount = 0;
        _host.SamplePageIndex = 0;
        _host.NotifySampleCounts();
        _host.NotifySamplePageState();
        _host.NotifyActiveSamplePositionTextChanged();
        _host.NotifyHasSamplesChanged();
    }

    public async Task LoadSamplePageAsync(
        int pageIndex,
        bool refreshActiveIndex = false,
        CancellationToken cancellationToken = default)
    {
        var loadGeneration = Interlocked.Increment(ref _samplePageLoadGeneration);
        var context = _host.Context;
        var gallery = context.Gallery;
        if (gallery is null)
        {
            _activeSampleIndex = -1;
            _host.InvalidateSampleThumbnailLoads();
            _host.Samples.Clear();
            _host.SamplePageItems.Clear();
            context.SampleCount = 0;
            context.AnnotatedCount = 0;
            _host.NotifySampleCounts();
            _host.NotifyHasSamplesChanged();
            _host.NotifyActiveSamplePositionTextChanged();
            _host.NotifySamplePageState();
            return;
        }

        if (refreshActiveIndex && context.ActiveSampleId is not null)
        {
            var indexResult = await _mediator.Send(
                new GetGallerySampleIndexQuery(gallery.Id, context.ActiveSampleId.Value),
                cancellationToken);
            if (loadGeneration != Volatile.Read(ref _samplePageLoadGeneration)
                || context.Gallery?.Id != gallery.Id)
            {
                return;
            }

            _activeSampleIndex = indexResult.IsSuccess ? indexResult.Value : -1;
            _host.NotifyActiveSamplePositionTextChanged();
            var targetPage = _activeSampleIndex >= 0
                ? _activeSampleIndex / LabelingConstants.SamplePageSize
                : pageIndex;
            if (targetPage != pageIndex)
            {
                _host.SamplePageIndex = targetPage;
                return;
            }
        }

        var skip = pageIndex * LabelingConstants.SamplePageSize;
        var pageResult = await _mediator.Send(
            new ListGallerySamplesQuery(
                gallery.Id,
                skip,
                LabelingConstants.SamplePageSize),
            cancellationToken);
        if (loadGeneration != Volatile.Read(ref _samplePageLoadGeneration)
            || context.Gallery?.Id != gallery.Id)
        {
            return;
        }

        if (pageResult.IsFailure || pageResult.Value is null)
        {
            _logger.LogWarning(
                "Failed to load sample page for gallery {GalleryId}: {Error}",
                gallery.Id,
                pageResult.Error);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var page = pageResult.Value;
        var existingThumbnails = _host.SamplePageItems.ToDictionary(row => row.Id, row => row.Thumbnail);
        DetachTrackedRows();
        _host.InvalidateSampleThumbnailLoads();
        _host.Samples.Clear();
        _host.SamplePageItems.Clear();

        var index = skip + 1;
        var anchorFileName = _host.AnchorFileName;
        foreach (var sample in page.Items)
        {
            var row = LabelingSampleRow.FromSample(
                sample,
                index++,
                sample.Id == context.ActiveSampleId,
                _annotations.GetImagePath(gallery.ProjectId, gallery.Id, sample.FileName));
            row.IsSelected = _host.SelectedSampleFileNames.Contains(sample.FileName);
            row.IsAnchor = !string.IsNullOrWhiteSpace(anchorFileName)
                && string.Equals(sample.FileName, anchorFileName, StringComparison.OrdinalIgnoreCase);
            row.SelectionChanged += OnTrackedRowSelectionChanged;
            _trackedSampleRows.Add(row);
            if (existingThumbnails.TryGetValue(sample.Id, out var thumbnail))
            {
                row.Thumbnail = thumbnail;
            }

            _host.Samples.Add(row);
            _host.SamplePageItems.Add(row);
        }

        context.SampleCount = page.TotalCount;
        context.AnnotatedCount = gallery.AnnotatedCount;
        _host.NotifySampleCounts();
        _host.NotifyHasSamplesChanged();
        _host.NotifyActiveSamplePositionTextChanged();
        _host.NotifySamplePageState();
        _host.ScheduleSampleThumbnailLoad();
    }

    public async Task UpdateActiveSampleIndexAsync()
    {
        var context = _host.Context;
        var gallery = context.Gallery;
        if (gallery is null || context.ActiveSampleId is null)
        {
            _activeSampleIndex = -1;
            _host.NotifyActiveSamplePositionTextChanged();
            return;
        }

        var indexResult = await _mediator.Send(
            new GetGallerySampleIndexQuery(gallery.Id, context.ActiveSampleId.Value));
        _activeSampleIndex = indexResult.IsSuccess ? indexResult.Value : -1;
        _host.NotifyActiveSamplePositionTextChanged();

        if (_activeSampleIndex < 0 || _host.SampleCount <= 0)
        {
            return;
        }

        var targetPage = _activeSampleIndex / LabelingConstants.SamplePageSize;
        if (_host.SamplePageIndex != targetPage)
        {
            _host.SamplePageIndex = targetPage;
        }
    }

    public void UpdateActiveSampleIndexFromCurrentPage(Guid sampleId)
    {
        var row = _host.SamplePageItems.FirstOrDefault(item => item.Id == sampleId);
        if (row is null)
        {
            return;
        }

        _activeSampleIndex = row.Index - 1;
        _host.NotifyActiveSamplePositionTextChanged();
    }

    public void UpdateSampleActiveStates()
    {
        var activeId = _host.Context.ActiveSampleId;
        foreach (var row in _host.SamplePageItems)
        {
            row.IsActive = row.Id == activeId;
        }
    }

    public void UpdateActiveSampleAnnotationState()
    {
        var context = _host.Context;
        if (context.ActiveSampleId is null)
        {
            return;
        }

        var row = _host.SamplePageItems.FirstOrDefault(item => item.Id == context.ActiveSampleId);
        if (row is null)
        {
            return;
        }

        var isAnnotated = !string.IsNullOrWhiteSpace(_host.ImageClass);
        if (row.IsAnnotated == isAnnotated)
        {
            return;
        }

        row.IsAnnotated = isAnnotated;
        _host.NotifyAnnotatedCountChanged();
    }

    private async Task NavigateSampleAsync(int offset)
    {
        var context = _host.Context;
        var gallery = context.Gallery;
        if (gallery is null || _host.SampleCount == 0 || context.ActiveSampleId is null || offset == 0)
        {
            return;
        }

        var currentIndex = _activeSampleIndex;
        if (currentIndex < 0)
        {
            var indexResult = await _mediator.Send(
                new GetGallerySampleIndexQuery(gallery.Id, context.ActiveSampleId.Value));
            currentIndex = indexResult.IsSuccess ? indexResult.Value : -1;
            if (currentIndex < 0)
            {
                return;
            }

            _activeSampleIndex = currentIndex;
        }

        var sampleCount = _host.SampleCount;
        var nextIndex = (currentIndex + offset % sampleCount + sampleCount) % sampleCount;
        if (nextIndex == currentIndex)
        {
            return;
        }

        var pageStart = _host.SamplePageIndex * LabelingConstants.SamplePageSize;
        var offsetInPage = nextIndex - pageStart;
        if (offsetInPage >= 0 && offsetInPage < _host.SamplePageItems.Count)
        {
            var row = _host.SamplePageItems[offsetInPage];
            _activeSampleIndex = nextIndex;
            _host.NotifyActiveSamplePositionTextChanged();
            await SelectSampleAsync(row.Id);
            return;
        }

        var pageResult = await _mediator.Send(new ListGallerySamplesQuery(gallery.Id, nextIndex, 1));
        var sample = pageResult.IsSuccess ? pageResult.Value?.Items.FirstOrDefault() : null;
        if (sample is null)
        {
            return;
        }

        _activeSampleIndex = nextIndex;
        _host.NotifyActiveSamplePositionTextChanged();
        await SelectSampleAsync(sample.Id);
    }

    private void ClampSamplePageIndex()
    {
        if (_host.SampleCount <= 0)
        {
            _host.SamplePageIndex = 0;
            return;
        }

        var maxPage = _host.SamplePageCount - 1;
        if (_host.SamplePageIndex > maxPage)
        {
            _host.SamplePageIndex = maxPage;
        }
    }

    private void DetachTrackedRows()
    {
        foreach (var row in _trackedSampleRows)
        {
            row.SelectionChanged -= OnTrackedRowSelectionChanged;
        }

        _trackedSampleRows.Clear();
    }

    private void OnTrackedRowSelectionChanged(LabelingSampleRow row) =>
        _host.HandleSampleRowSelectionChanged(row);
}

/// <summary>Host surface for <see cref="LabelingSampleNavigation"/>.</summary>
public interface ILabelingSampleNavigationHost
{
    LabelingWorkspaceContext Context { get; }

    string? ImageClass { get; }

    ObservableCollection<LabelingSampleRow> Samples { get; }

    ObservableCollection<LabelingSampleRow> SamplePageItems { get; }

    HashSet<string> SelectedSampleFileNames { get; }

    int SamplePageIndex { get; set; }

    string? AnchorFileName { get; set; }

    int SampleCount { get; }

    int SamplePageCount { get; }

    Task OpenSampleAsync(Guid sampleId);

    void InvalidateSampleThumbnailLoads();

    void ScheduleSampleThumbnailLoad();

    void HandleSampleRowSelectionChanged(LabelingSampleRow row);

    void NotifyActiveSamplePositionTextChanged();

    void NotifySamplePageState();

    void NotifySampleCounts();

    void NotifyHasSamplesChanged();

    void NotifyAnnotatedCountChanged();

    void NotifyAnchorCommandsChanged();
}
