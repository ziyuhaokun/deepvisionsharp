using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.IO;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels.Gallery;

public sealed partial class GalleryTransferViewModel : ObservableObject
{
    private readonly ILocalFileDialogService _fileDialog;
    private readonly IGalleryTransferService _transferService;
    private readonly GalleryListViewModel _list;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<GalleryTransferViewModel> _logger;

    public GalleryTransferViewModel(
        ILocalFileDialogService fileDialog,
        IGalleryTransferService transferService,
        GalleryListViewModel list,
        ShellSnackbarNotifier snackbar,
        ILogger<GalleryTransferViewModel> logger)
    {
        _fileDialog = fileDialog;
        _transferService = transferService;
        _list = list;
        _snackbar = snackbar;
        _logger = logger;
        _list.BusyStateAffectingCommandsChanged += NotifyBusyCommands;
    }

    [ObservableProperty]
    private bool _isImporting;

    [ObservableProperty]
    private string _importStage = string.Empty;

    [ObservableProperty]
    private int _importProcessed;

    [ObservableProperty]
    private int _importTotal;

    [ObservableProperty]
    private string? _importCurrentFile;

    public bool IsNotImporting => !IsImporting;

    public double ImportProgressValue => ImportTotal > 0 ? Math.Clamp((double)ImportProcessed / ImportTotal, 0, 1) : 0;

    public string ImportProgressText => ImportTotal > 0 ? $"{ImportProcessed} / {ImportTotal}" : "准备中";

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ImportGalleryAsync()
    {
        if (_list.CurrentProjectId is null)
        {
            _snackbar.ShowWarning("请先选择项目。", "导入");
            return;
        }

        var files = await _fileDialog.SelectFilesAsync(
            "图库文件|*.gallery|SQLite 文件|*.sqlite;*.db|所有文件|*.*",
            allowMultiSelect: false);
        if (files is not { Length: > 0 })
        {
            _snackbar.ShowInfo("已取消导入图库。", "导入");
            return;
        }

        await RunTransferAsync(
            "准备导入图库",
            progress => _transferService.ImportGalleryAsync(_list.CurrentProjectId.Value, files[0], progress),
            "导入图库");
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ImportImagesAsync(Guid galleryId)
    {
        var files = await _fileDialog.SelectFilesAsync(
            "图像与标注文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.json|图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*");
        if (files is not { Length: > 0 })
        {
            _snackbar.ShowInfo("已取消导入图像。", "导入");
            return;
        }

        await RunTransferAsync(
            "准备导入图像",
            progress => _transferService.ImportSamplesFromFilesAsync(galleryId, files, progress),
            "导入图像");
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ImportFolderAsync(Guid galleryId)
    {
        var folderPath = await _fileDialog.SelectFolderAsync();
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            _snackbar.ShowInfo("已取消导入目录。", "导入");
            return;
        }

        await RunTransferAsync(
            "准备导入目录",
            progress => _transferService.ImportSamplesFromFolderAsync(galleryId, folderPath, recursive: false, progress),
            "导入目录");
    }

    [RelayCommand(CanExecute = nameof(CanRunGalleryOperation))]
    private async Task ExportGalleryAsync(Guid galleryId)
    {
        var gallery = _list.Galleries.FirstOrDefault(item => item.Id == galleryId);
        if (gallery is null)
        {
            _snackbar.ShowWarning("未找到图库。", "导出");
            return;
        }

        var defaultName = $"{SanitizeFileName(_list.ProjectName)}-{SanitizeFileName(gallery.Name)}.gallery";
        var outputPath = await _fileDialog.SaveFileAsync("图库文件|*.gallery|所有文件|*.*", defaultName);
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            _snackbar.ShowInfo("已取消导出图库。", "导出");
            return;
        }

        await RunTransferAsync(
            "准备导出图库",
            progress => _transferService.ExportGalleryAsync(galleryId, outputPath, progress),
            "导出图库");
    }

    private bool CanRunGalleryOperation() => _list.CanRunGalleryOperation();

    private void NotifyBusyCommands()
    {
        ImportGalleryCommand.NotifyCanExecuteChanged();
        ImportImagesCommand.NotifyCanExecuteChanged();
        ImportFolderCommand.NotifyCanExecuteChanged();
        ExportGalleryCommand.NotifyCanExecuteChanged();
    }

    private async Task RunTransferAsync(
        string initialStage,
        Func<IProgress<GalleryTransferProgress>, Task<GalleryTransferResult>> operation,
        string actionTitle)
    {
        IsImporting = true;
        ImportStage = initialStage;
        ImportProcessed = 0;
        ImportTotal = 0;
        ImportCurrentFile = null;
        _list.SetBlockedByTransfer(true);
        _list.NotifyBusyStateChanged();

        try
        {
            _logger.LogInformation("Gallery transfer started: {Stage}", initialStage);
            var progress = new Progress<GalleryTransferProgress>(value =>
            {
                ImportStage = value.Stage;
                ImportProcessed = Math.Max(0, value.Processed);
                ImportTotal = Math.Max(0, value.Total);
                ImportCurrentFile = value.CurrentFile;
            });

            var result = await operation(progress);
            NotifyTransferResult(actionTitle, result);
            _logger.LogInformation(
                "Gallery transfer completed: {Stage}. Succeeded={Succeeded}, Total={Total}, Added={Added}, Failed={Failed}, Skipped={Skipped}",
                initialStage,
                result.Succeeded,
                result.Total,
                result.Added,
                result.Failed,
                result.Skipped);
            if (result.Errors.Count > 0)
            {
                _logger.LogWarning("Gallery transfer reported first error: {Error}", result.Errors[0]);
            }

            await _list.RefreshCurrentProjectGalleriesAsync();
            await _list.RecalculateProjectProgressAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gallery transfer failed: {Stage}", initialStage);
            _snackbar.ShowError($"操作失败：{ex.Message}", actionTitle);
        }
        finally
        {
            IsImporting = false;
            ImportCurrentFile = null;
            _list.SetBlockedByTransfer(false);
            _list.NotifyBusyStateChanged();
        }
    }

    private void NotifyTransferResult(string actionTitle, GalleryTransferResult result)
    {
        var message = FormatImportMessage(actionTitle, result);
        if (result.Errors.Count > 0 || result.Failed > 0 || (result.Total == 0 && !result.Succeeded))
        {
            _snackbar.ShowWarning(message, actionTitle);
        }
        else if (result.Succeeded && result.Added > 0)
        {
            _snackbar.ShowSuccess(message, actionTitle);
        }
        else
        {
            _snackbar.ShowInfo(message, actionTitle);
        }
    }

    partial void OnIsImportingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotImporting));
        NotifyBusyCommands();
        _list.NotifyBusyStateChanged();
    }

    partial void OnImportProcessedChanged(int value)
    {
        OnPropertyChanged(nameof(ImportProgressValue));
        OnPropertyChanged(nameof(ImportProgressText));
    }

    partial void OnImportTotalChanged(int value)
    {
        OnPropertyChanged(nameof(ImportProgressValue));
        OnPropertyChanged(nameof(ImportProgressText));
    }

    private static string FormatImportMessage(string action, GalleryTransferResult result)
    {
        if (result.Total == 0 && result.Errors.Count > 0)
        {
            return $"{action}未完成：{result.Errors[0]}";
        }

        if (result.Succeeded && result.Failed == 0 && result.Skipped == 0)
        {
            return $"{action}成功：处理 {result.Added} / {result.Total} 项";
        }

        var firstError = result.Errors.Count > 0 ? $"，{result.Errors[0]}" : string.Empty;
        return $"{action}完成：成功 {result.Added}，失败 {result.Failed}，跳过 {result.Skipped}{firstError}";
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "gallery";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "gallery" : sanitized;
    }
}
