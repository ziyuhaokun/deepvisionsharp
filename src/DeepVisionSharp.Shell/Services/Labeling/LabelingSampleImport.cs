using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// File-dialog sample import for the labeling sample list.
/// Owned by <see cref="LabelingSampleListViewModel"/>.
/// </summary>
public sealed class LabelingSampleImport
{
    private readonly ILabelingSampleImportHost _host;
    private readonly IMediator _mediator;
    private readonly IGalleryTransferService _galleryTransfer;
    private readonly ILocalFileDialogService _fileDialog;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger _logger;

    public LabelingSampleImport(
        ILabelingSampleImportHost host,
        IMediator mediator,
        IGalleryTransferService galleryTransfer,
        ILocalFileDialogService fileDialog,
        ShellSnackbarNotifier snackbar,
        ILogger logger)
    {
        _host = host;
        _mediator = mediator;
        _galleryTransfer = galleryTransfer;
        _fileDialog = fileDialog;
        _snackbar = snackbar;
        _logger = logger;
    }

    public async Task ImportImagesAsync()
    {
        var gallery = _host.Context.Gallery;
        if (gallery is null)
        {
            _snackbar.ShowWarning("请先选择图库。", "导入");
            return;
        }

        var files = await _fileDialog.SelectFilesAsync(
            "图像与标注文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.json|图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*");
        if (files is not { Length: > 0 })
        {
            _snackbar.ShowInfo("已取消导入图像。", "导入");
            return;
        }

        var galleryId = gallery.Id;
        var previousSampleCount = gallery.SampleCount;
        _host.CancelPendingSave();
        _host.Context.IsImporting = true;

        try
        {
            var result = await _galleryTransfer.ImportSamplesFromFilesAsync(galleryId, files, null);
            var importMessage = FormatImportMessage("导入图像", result);
            if (result.Total == 0 && result.Errors.Count > 0)
            {
                _snackbar.ShowError(importMessage, "导入");
            }
            else if (result.Succeeded)
            {
                _snackbar.ShowSuccess(importMessage, "导入");
            }
            else
            {
                _snackbar.ShowWarning(importMessage, "导入");
            }

            await _host.RefreshFromGalleryAsync();

            if (_host.Context.SampleCount > 0
                && (previousSampleCount == 0 || _host.Context.ActiveSampleId is null))
            {
                var firstPageResult = await _mediator.Send(new ListGallerySamplesQuery(galleryId, 0, 1));
                var firstSampleId = firstPageResult.IsSuccess
                    ? firstPageResult.Value?.Items.FirstOrDefault()?.Id
                    : null;
                if (firstSampleId is not null)
                {
                    await _host.SelectSampleAsync(firstSampleId.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import images into gallery {GalleryId}", galleryId);
            _snackbar.ShowError($"导入图像失败：{ex.Message}", "导入");
        }
        finally
        {
            _host.Context.IsImporting = false;
        }
    }

    private static string FormatImportMessage(string action, GalleryTransferResult result)
    {
        if (result.Total == 0 && result.Errors.Count > 0)
        {
            return $"{action}未完成：{result.Errors[0]}";
        }

        if (result.Succeeded)
        {
            return $"{action}完成：新增 {result.Added} 个，跳过 {result.Skipped} 个。";
        }

        return $"{action}部分完成：新增 {result.Added} 个，失败 {result.Failed} 个，跳过 {result.Skipped} 个。";
    }
}

/// <summary>Host surface for <see cref="LabelingSampleImport"/>.</summary>
public interface ILabelingSampleImportHost
{
    LabelingWorkspaceContext Context { get; }

    void CancelPendingSave();

    Task RefreshFromGalleryAsync(CancellationToken cancellationToken = default);

    Task SelectSampleAsync(Guid sampleId);
}
