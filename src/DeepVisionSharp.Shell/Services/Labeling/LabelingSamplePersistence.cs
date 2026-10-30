using System.IO;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// Sample open / save / debounce pipeline for the labeling editor.
/// Serializes saves and cancels superseded opens so annotations never write to the wrong file.
/// </summary>
public sealed class LabelingSamplePersistence : IDisposable
{
    private const int SaveDebounceMs = 450;

    private readonly ILabelingSamplePersistenceHost _host;
    private readonly IAnnotationWorkflowService _annotations;
    private readonly IMediator _mediator;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger _logger;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Func<Task> _refreshGalleryAsync;

    private CancellationTokenSource? _saveDebounceCts;
    private CancellationTokenSource? _openSampleCts;
    private readonly SemaphoreSlim _saveGate = new(1, 1);
    private readonly SemaphoreSlim _openSampleGate = new(1, 1);

    public LabelingSamplePersistence(
        ILabelingSamplePersistenceHost host,
        IAnnotationWorkflowService annotations,
        IMediator mediator,
        ShellSnackbarNotifier snackbar,
        ILogger logger,
        IUiDispatcher uiDispatcher,
        Func<Task> refreshGalleryAsync)
    {
        _host = host;
        _annotations = annotations;
        _mediator = mediator;
        _snackbar = snackbar;
        _logger = logger;
        _uiDispatcher = uiDispatcher;
        _refreshGalleryAsync = refreshGalleryAsync;
    }

    public async Task OpenSampleAsync(Guid sampleId)
    {
        var currentCts = new CancellationTokenSource();
        var previousCts = Interlocked.Exchange(ref _openSampleCts, currentCts);
        try
        {
            previousCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        var enteredGate = false;
        try
        {
            await _openSampleGate.WaitAsync(currentCts.Token);
            enteredGate = true;
            await OpenSampleCoreAsync(sampleId, currentCts.Token);
        }
        catch (OperationCanceledException) when (currentCts.IsCancellationRequested)
        {
            _logger.LogDebug("Open sample {SampleId} was superseded", sampleId);
        }
        finally
        {
            if (enteredGate)
            {
                _openSampleGate.Release();
            }

            Interlocked.CompareExchange(ref _openSampleCts, null, currentCts);
            currentCts.Dispose();
        }
    }

    /// <summary>
    /// Cancels debounced save and waits for any in-flight save to finish after writing the current sample.
    /// </summary>
    public async Task FlushPendingSaveAsync()
    {
        CancelPendingSave();
        var context = _host.Context;
        if (context.Gallery is null
            || context.ActiveSampleId is null
            || string.IsNullOrWhiteSpace(context.ActiveFileName))
        {
            // Still wait out any in-flight save that already captured identity under the gate.
            await _saveGate.WaitAsync();
            _saveGate.Release();
            return;
        }

        await SaveCurrentSampleAsync(refreshGallery: false, notifyUser: false);
    }

    public void CancelPendingSave()
    {
        try
        {
            _saveDebounceCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
        _saveDebounceCts?.Dispose();
        _saveDebounceCts = null;
    }

    public void CancelPendingOpen()
    {
        var previous = Interlocked.Exchange(ref _openSampleCts, null);
        try
        {
            previous?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public async Task<bool> SaveCurrentSampleAsync(bool refreshGallery, bool notifyUser = false)
    {
        await _saveGate.WaitAsync();
        try
        {
            var context = _host.Context;
            var gallery = context.Gallery;
            if (gallery is null || context.ActiveSampleId is null || string.IsNullOrWhiteSpace(context.ActiveFileName))
            {
                if (notifyUser)
                {
                    _snackbar.ShowWarning("请先选择图库和样本。", "保存");
                }

                return false;
            }

            // Capture identity and payload under the gate so a concurrent open cannot retarget this write.
            var galleryId = gallery.Id;
            var sampleId = context.ActiveSampleId.Value;
            var fileName = context.ActiveFileName;
            var imageWidth = context.ImageWidth;
            var imageHeight = context.ImageHeight;
            var imageClass = _host.ImageClass?.Trim();
            var shapes = _host.Shapes.ToArray();
            var isReviewed = _host.IsReviewed;

            context.IsSaving = true;

            // Abort if the editor switched samples after we entered the gate (should be rare after Flush).
            if (context.ActiveSampleId != sampleId
                || !string.Equals(context.ActiveFileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Skipping annotation save for {FileName}: active sample changed during save",
                    fileName);
                return false;
            }

            await _annotations.SaveAsync(
                galleryId,
                fileName,
                imageWidth,
                imageHeight,
                imageClass,
                shapes,
                isReviewed);

            if (refreshGallery)
            {
                await _refreshGalleryAsync();
            }

            if (notifyUser)
            {
                var detail = shapes.Length > 0
                    ? $"{shapes.Length} 个目标"
                    : isReviewed
                        ? "无目标"
                        : string.IsNullOrWhiteSpace(imageClass)
                            ? "未分类"
                            : $"分类 {imageClass}";
                _snackbar.ShowSuccess($"已保存「{fileName}」：{detail}。", "保存");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save annotations for {FileName}", _host.Context.ActiveFileName);
            _snackbar.ShowError($"保存失败：{ex.Message}", "保存");
            return false;
        }
        finally
        {
            _host.Context.IsSaving = false;
            _saveGate.Release();
        }
    }

    public async Task RequestSaveAsync()
    {
        CancelPendingSave();
        var sampleId = _host.Context.ActiveSampleId;
        var fileName = _host.Context.ActiveFileName;
        if (sampleId is null || string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        _saveDebounceCts = new CancellationTokenSource();
        var token = _saveDebounceCts.Token;

        try
        {
            await Task.Delay(SaveDebounceMs, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            // Drop stale debounced saves after sample switch (parity with training draft job-id check).
            if (_host.Context.ActiveSampleId != sampleId
                || !string.Equals(_host.Context.ActiveFileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await SaveCurrentSampleAsync(refreshGallery: false);
            _host.RaiseDebouncedSaveCompleted();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        CancelPendingSave();
        CancelPendingOpen();
        _saveGate.Dispose();
        _openSampleGate.Dispose();
    }

    private async Task OpenSampleCoreAsync(Guid sampleId, CancellationToken cancellationToken)
    {
        // Flush any in-flight or debounced save against the *current* sample identity
        // before switching ActiveFileName, so a class cannot land on the wrong file.
        await FlushPendingSaveAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var context = _host.Context;
        var gallery = context.Gallery;
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到样本。", "标记");
            return;
        }

        var sampleResult = await _mediator.Send(
            new GetGallerySampleQuery(gallery.Id, sampleId),
            cancellationToken);
        if (sampleResult.IsFailure || sampleResult.Value is null)
        {
            _snackbar.ShowWarning(sampleResult.Error ?? "未找到样本。", "标记");
            return;
        }

        var sample = sampleResult.Value;
        var imagePath = _annotations.GetImagePath(gallery.ProjectId, gallery.Id, sample.FileName);

        var image = await context.LoadImageAsync(imagePath, cancellationToken);
        if (image is null)
        {
            _snackbar.ShowError($"图像文件不存在：{Path.GetFileName(imagePath)}", "标记");
            return;
        }

        var document = await LoadAnnotationsAsync(gallery.Id, sample.FileName, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        // Everything above only reads. Identity, image and image class are committed together on
        // the UI thread as the last step, so an open cancelled at any earlier point leaves the
        // previous sample — and its class — fully intact instead of pairing the new file name
        // with the old class on the next flush.
        await _uiDispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _host.ApplySampleState(
                sample.Id,
                sample.FileName,
                imagePath,
                image,
                document.ImageWidth,
                document.ImageHeight,
                document.ImageClass,
                document.Boxes,
                document.IsReviewed);
        }).ConfigureAwait(false);

        _logger.LogInformation("Selected sample {SampleId} in gallery {GalleryId}", sample.Id, gallery.Id);
    }

    private async Task<AnnotationDocument> LoadAnnotationsAsync(
        Guid galleryId,
        string fileName,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new LoadAnnotationsQuery(galleryId, fileName),
            cancellationToken);
        return result.Value ?? new AnnotationDocument(0, 0);
    }
}

/// <summary>Host surface required by <see cref="LabelingSamplePersistence"/> (implemented by the editor VM).</summary>
public interface ILabelingSamplePersistenceHost
{
    LabelingWorkspaceContext Context { get; }

    /// <summary>Class currently shown in the editor; read when saving the active sample.</summary>
    string? ImageClass { get; }

    /// <summary>Boxes currently shown in the editor; read when saving the active sample.</summary>
    IReadOnlyList<AnnotationShape> Shapes { get; }

    /// <summary>Detection review mark; read when saving the active sample.</summary>
    bool IsReviewed { get; }

    /// <summary>
    /// Publishes a fully loaded sample (identity, image, dimensions, class, boxes) in one synchronous step.
    /// Must run on the UI thread.
    /// </summary>
    void ApplySampleState(
        Guid sampleId,
        string fileName,
        string imagePath,
        LoadedSampleImage image,
        double imageWidth,
        double imageHeight,
        string? imageClass,
        IReadOnlyList<AnnotationShape> shapes,
        bool isReviewed);

    void RaiseDebouncedSaveCompleted();
}
