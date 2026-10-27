using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// Concurrent sample-page thumbnail decode with generation invalidation.
/// Superseded loads never write back to UI rows.
/// </summary>
public sealed class LabelingSampleThumbnailLoader
{
    private readonly ISampleThumbnailService _sampleThumbnails;
    private readonly IImageSourceFactory _imageSources;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ILogger _logger;
    private readonly Func<bool> _isPanelExpanded;
    private readonly Func<IReadOnlyList<LabelingSampleRow>> _getPageItems;

    private int _loadGeneration;
    private CancellationTokenSource? _cts = new();

    public LabelingSampleThumbnailLoader(
        ISampleThumbnailService sampleThumbnails,
        IImageSourceFactory imageSources,
        IUiDispatcher uiDispatcher,
        ILogger logger,
        Func<bool> isPanelExpanded,
        Func<IReadOnlyList<LabelingSampleRow>> getPageItems)
    {
        _sampleThumbnails = sampleThumbnails;
        _imageSources = imageSources;
        _uiDispatcher = uiDispatcher;
        _logger = logger;
        _isPanelExpanded = isPanelExpanded;
        _getPageItems = getPageItems;
    }

    /// <summary>
    /// Schedules thumbnail decode for rows on the current page that still lack an image.
    /// No-op when the panel is collapsed or every row already has a thumbnail.
    /// </summary>
    public void Schedule()
    {
        if (!_isPanelExpanded())
        {
            return;
        }

        var pageItems = _getPageItems();
        if (pageItems.Count == 0 || !pageItems.Any(row => row.Thumbnail is null))
        {
            return;
        }

        var generation = Volatile.Read(ref _loadGeneration);
        ShellAsync.FireAndForget(
            () => LoadPageAsync(generation),
            _logger,
            "LoadSampleThumbnails");
    }

    /// <summary>
    /// Cancels in-flight thumbnail decode/IO (page unload, page change, gallery reset).
    /// </summary>
    public void Cancel()
    {
        Interlocked.Increment(ref _loadGeneration);
        var previous = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        previous?.Dispose();
    }

    private async Task LoadPageAsync(int generation)
    {
        var rows = _getPageItems().Where(row => row.Thumbnail is null).ToArray();
        if (rows.Length == 0 || generation != Volatile.Read(ref _loadGeneration))
        {
            return;
        }

        var token = _cts?.Token ?? CancellationToken.None;
        try
        {
            await Parallel.ForEachAsync(
                rows,
                new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = 4
                },
                async (row, itemCancellationToken) =>
                {
                    if (generation != Volatile.Read(ref _loadGeneration))
                    {
                        return;
                    }

                    await LoadRowAsync(row, generation, itemCancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private async Task LoadRowAsync(
        LabelingSampleRow row,
        int generation,
        CancellationToken cancellationToken)
    {
        if (generation != Volatile.Read(ref _loadGeneration) || row.Thumbnail is not null)
        {
            return;
        }

        try
        {
            await _uiDispatcher.InvokeAsync(() => row.IsThumbnailLoading = true).ConfigureAwait(false);

            var bytes = await _sampleThumbnails
                .GetThumbnailAsync(row.ImagePath, LabelingConstants.SampleThumbnailMaxEdge, cancellationToken)
                .ConfigureAwait(false);

            if (generation != Volatile.Read(ref _loadGeneration)
                || cancellationToken.IsCancellationRequested
                || bytes is null)
            {
                return;
            }

            var image = _imageSources.FromBytes(bytes);
            if (image is not null)
            {
                await _uiDispatcher.InvokeAsync(() => row.Thumbnail = image).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load thumbnail for {FileName}", row.FileName);
        }
        finally
        {
            if (generation == Volatile.Read(ref _loadGeneration))
            {
                await _uiDispatcher.InvokeAsync(() => row.IsThumbnailLoading = false).ConfigureAwait(false);
            }
        }
    }
}
