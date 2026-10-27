using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediatR;
using Microsoft.Extensions.Logging;
using System.IO;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Shell.Services;

namespace DeepVisionSharp.Shell.ViewModels.Dashboard;

public sealed partial class DashboardProjectTransferViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly ILocalFileDialogService _fileDialog;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<DashboardProjectTransferViewModel> _logger;
    private readonly Func<Task> _refreshProjectsAsync;

    public DashboardProjectTransferViewModel(
        IMediator mediator,
        ILocalFileDialogService fileDialog,
        ShellSnackbarNotifier snackbar,
        ILogger<DashboardProjectTransferViewModel> logger,
        Func<Task> refreshProjectsAsync)
    {
        _mediator = mediator;
        _fileDialog = fileDialog;
        _snackbar = snackbar;
        _logger = logger;
        _refreshProjectsAsync = refreshProjectsAsync;
    }

    [ObservableProperty]
    private bool _isTransferring;

    /// <summary>导入/导出进行中时通知协调者，以便禁用并发的项目操作（删除/进入）。</summary>
    public event Action<bool>? IsTransferringChanged;

    [ObservableProperty]
    private string _transferStage = string.Empty;

    [ObservableProperty]
    private int _transferProcessed;

    [ObservableProperty]
    private int _transferTotal;

    [ObservableProperty]
    private string? _transferCurrentFile;

    public bool IsNotTransferring => !IsTransferring;

    public double TransferProgressValue =>
        TransferTotal > 0 ? Math.Clamp((double)TransferProcessed / TransferTotal, 0, 1) : 0;

    public string TransferProgressText =>
        TransferTotal > 0 ? $"{TransferProcessed} / {TransferTotal}" : "准备中";

    public int TransferProgressPercent =>
        TransferTotal > 0 ? (int)Math.Round(TransferProgressValue * 100) : 0;

    public string TransferProgressPercentText =>
        TransferTotal > 0 ? $"{TransferProgressPercent}%" : "—";

    public string TransferCurrentFileDisplay =>
        string.IsNullOrWhiteSpace(TransferCurrentFile)
            ? "正在准备文件…"
            : Path.GetFileName(TransferCurrentFile);

    [RelayCommand(CanExecute = nameof(CanRunProjectTransfer))]
    private async Task ExportProjectAsync(Guid projectId)
    {
        var projectResult = await _mediator.Send(new GetProjectByIdQuery(projectId));
        if (projectResult.IsFailure)
        {
            _snackbar.ShowError(projectResult.Error ?? "加载项目失败。", "导出");
            return;
        }

        if (projectResult.Value is null)
        {
            _snackbar.ShowWarning("未找到项目。", "导出");
            return;
        }

        var project = projectResult.Value;
        var defaultName = $"{SanitizeFileName(project.Name)}.project";
        var filePath = await _fileDialog.SaveFileAsync("项目文件|*.project|所有文件|*.*", defaultName);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _snackbar.ShowInfo("已取消导出。", "导出");
            return;
        }

        var result = await RunProjectTransferAsync(
            "准备导出项目",
            Path.GetFileName(filePath),
            progress => _mediator.Send(new ExportProjectToSqliteCommand(projectId, filePath, progress)),
            "导出",
            refreshAfter: false);

        if (result is null)
        {
            return;
        }

        _logger.LogInformation("Exported project {ProjectId} to {FilePath}", project.Id, filePath);
    }

    [RelayCommand(CanExecute = nameof(CanRunProjectTransfer))]
    private async Task ImportProjectAsync()
    {
        var files = await _fileDialog.SelectFilesAsync("项目文件|*.project|所有文件|*.*", allowMultiSelect: false);
        if (files is not { Length: > 0 })
        {
            _snackbar.ShowInfo("已取消导入。", "导入");
            return;
        }

        var filePath = files[0];
        var transfer = await RunProjectTransferAsync(
            "准备导入项目",
            Path.GetFileName(filePath),
            progress => _mediator.Send(new ImportProjectFromSqliteCommand(filePath, progress)),
            "导入",
            refreshAfter: true);

        if (transfer is null)
        {
            return;
        }

        _logger.LogInformation("Imported project from {FilePath}", filePath);
    }

    private bool CanRunProjectTransfer() => !IsTransferring;

    private async Task<ProjectTransferResult?> RunProjectTransferAsync(
        string initialStage,
        string? initialFile,
        Func<IProgress<ProjectTransferProgress>, Task<Result<ProjectTransferResult>>> operation,
        string actionTitle,
        bool refreshAfter)
    {
        IsTransferring = true;
        TransferStage = initialStage;
        TransferProcessed = 0;
        TransferTotal = 0;
        TransferCurrentFile = initialFile;

        try
        {
            _logger.LogInformation("Project transfer started: {Stage}", initialStage);

            var progress = new Progress<ProjectTransferProgress>(value =>
            {
                TransferStage = value.Stage;
                TransferProcessed = Math.Max(0, value.Processed);
                TransferTotal = Math.Max(0, value.Total);
                TransferCurrentFile = value.CurrentFile ?? TransferCurrentFile;
            });

            var result = await operation(progress);
            if (result.IsFailure || result.Value is null)
            {
                _snackbar.ShowError(result.Error ?? $"{actionTitle}失败。", actionTitle);
                return null;
            }

            var transfer = result.Value;
            if (transfer.Succeeded && transfer.Failed == 0)
            {
                _snackbar.ShowSuccess($"{actionTitle}成功（{transfer.Added}/{transfer.Total} 样本）。", actionTitle);
            }
            else if (transfer.Succeeded)
            {
                _snackbar.ShowWarning($"{actionTitle}部分成功：成功 {transfer.Added}，失败 {transfer.Failed}。", actionTitle);
            }
            else
            {
                var errorSummary = transfer.Errors.Count > 0 ? transfer.Errors[0] : "未知错误";
                _snackbar.ShowWarning($"{actionTitle}完成但存在问题：{errorSummary}", actionTitle);
            }

            if (refreshAfter)
            {
                await _refreshProjectsAsync();
            }

            return transfer;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Project transfer failed: {Stage}", initialStage);
            _snackbar.ShowError($"操作失败：{ex.Message}", actionTitle);
            return null;
        }
        finally
        {
            IsTransferring = false;
            TransferCurrentFile = null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "project";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "project" : sanitized;
    }

    partial void OnIsTransferringChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotTransferring));
        IsTransferringChanged?.Invoke(value);
        ImportProjectCommand.NotifyCanExecuteChanged();
        ExportProjectCommand.NotifyCanExecuteChanged();
    }

    partial void OnTransferProcessedChanged(int value) => NotifyTransferProgressChanged();

    partial void OnTransferTotalChanged(int value) => NotifyTransferProgressChanged();

    partial void OnTransferCurrentFileChanged(string? value) =>
        OnPropertyChanged(nameof(TransferCurrentFileDisplay));

    private void NotifyTransferProgressChanged()
    {
        OnPropertyChanged(nameof(TransferProgressValue));
        OnPropertyChanged(nameof(TransferProgressText));
        OnPropertyChanged(nameof(TransferProgressPercent));
        OnPropertyChanged(nameof(TransferProgressPercentText));
    }
}
