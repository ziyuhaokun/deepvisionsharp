using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MediatR;

using System.IO;

using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.Services.Training;

namespace DeepVisionSharp.Shell.ViewModels.Training;

public sealed partial class TrainingViewModel : ObservableObject, IDisposable
{
    private readonly TrainingJobsViewModel _jobs;
    private readonly TrainingConfigViewModel _config;
    private readonly TrainingMonitorViewModel _monitor;
    private readonly TrainingLogViewModel _log;
    private readonly TrainingWorkspaceCoordinator _workspace;
    private readonly IMediator _mediator;
    private readonly IProcessLauncher _processLauncher;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IAppDialogService _appDialogService;
    private readonly TrainingJobRenameDialogState _renameDialog = new();

    public TrainingViewModel(
        TrainingWorkspaceCoordinator workspace,
        IMediator mediator,
        IProcessLauncher processLauncher,
        ShellSnackbarNotifier snackbar,
        IAppDialogService appDialogService)
    {
        _workspace = workspace;
        _jobs = workspace.JobList;
        _config = workspace.Config;
        _monitor = workspace.Monitor;
        _log = workspace.Log;
        _mediator = mediator;
        _processLauncher = processLauncher;
        _snackbar = snackbar;
        _appDialogService = appDialogService;
        _workspace.CommandStatesChanged += NotifyDeleteCommandsCanExecuteChanged;
    }

    public TrainingJobsViewModel JobList => _jobs;

    public TrainingConfigViewModel Config => _config;

    public TrainingMonitorViewModel Monitor => _monitor;

    public TrainingLogViewModel Log => _log;

    private bool CanConfirmDeleteJob => _jobs.SelectedJob is not null && CanDeleteJob(_jobs.SelectedJob);

    [RelayCommand(CanExecute = nameof(CanConfirmDeleteJob))]
    private async Task ConfirmDeleteJobAsync()
    {
        if (_jobs.SelectedJob is null)
        {
            return;
        }

        await ConfirmDeleteJobByIdAsync(_jobs.SelectedJob.Id);
    }

    [RelayCommand]
    private async Task ConfirmDeleteJobByIdAsync(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return;
        }

        if (!CanDeleteJob(job))
        {
            _snackbar.ShowWarning("训练准备中或运行中无法删除，请先停止。", "训练");
            return;
        }

        _jobs.SelectJobById(jobId);
        var request = new ConfirmDialogRequest(
            $"即将删除训练任务「{job.Name}」及其训练数据、日志、预览和中间模型。\n\n已保存到模型库的模型不会被删除。此操作不可恢复。");
        var result = await _appDialogService.ShowAsync(
            AppDialogKind.Confirm,
            request,
            new DialogOptions(
                Title: "确认删除训练任务",
                PrimaryButtonText: "删除",
                DefaultButton: AppDialogDefaultButton.Close));


        if (result == AppDialogResult.Primary)
        {
            await _jobs.DeleteJobAsync(jobId);
        }
    }

    [RelayCommand]
    private async Task DuplicateJobByIdAsync(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return;
        }

        if (!TrainingJobActionRules.CanDuplicate(job))
        {
            _snackbar.ShowWarning("仅已完成、失败或已取消的任务可复制。", "训练");
            return;
        }

        _jobs.SelectJobById(jobId);
        await _jobs.DuplicateJobAsync(jobId);
    }

    [RelayCommand]
    private async Task RenameJobByIdAsync(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return;
        }

        if (!TrainingJobActionRules.CanRename(job))
        {
            _snackbar.ShowWarning("仅草稿任务可重命名。", "训练");
            return;
        }

        _jobs.SelectJobById(jobId);
        _renameDialog.Reset(job.Name);

        while (true)
        {
            var result = await _appDialogService.ShowAsync(
                AppDialogKind.RenameTrainingJob,
                _renameDialog,
                new DialogOptions(Title: "重命名训练任务", PrimaryButtonText: "保存"));
            if (result != AppDialogResult.Primary)
            {
                return;
            }

            var name = _renameDialog.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _renameDialog.SetDialogError("请输入训练任务名称。");
                continue;
            }

            var renameResult = await _mediator.Send(new RenameTrainingJobCommand(jobId, name));
            if (renameResult.IsFailure)
            {
                _renameDialog.SetDialogError(renameResult.Error ?? "重命名失败。");
                continue;
            }

            await _jobs.ReloadJobsAsync();
            _jobs.SelectJobById(jobId);
            _config.NotifyDetailState();
            _snackbar.ShowSuccess("训练任务已重命名。", "训练");
            return;
        }
    }

    [RelayCommand]
    private async Task StopJobByIdAsync(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return;
        }

        if (!_workspace.CanStopJob(job))
        {
            _snackbar.ShowWarning("当前任务未在运行。", "训练");
            return;
        }

        _jobs.SelectJobById(jobId);
        await _config.StopTrainingCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private async Task SaveAsModelByIdAsync(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return;
        }

        if (!TrainingJobActionRules.CanSaveAsModel(job))
        {
            _snackbar.ShowWarning("仅训练成功且存在模型文件的任务可保存为模型。", "训练");
            return;
        }

        _jobs.SelectJobById(jobId);
        await _config.SaveAsModelCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ShowLogsById(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null)
        {
            return;
        }

        if (!TrainingJobActionRules.CanShowLogs(job))
        {
            return;
        }

        _jobs.SelectJobById(jobId);
        _monitor.StartLogTracking(job.Id, job.LogFile, job);
        _monitor.EnterRunMode(TrainingRunSection.Logs);
    }

    [RelayCommand]
    private void OpenOutputFolderById(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null || string.IsNullOrWhiteSpace(job.OutputRoot) || !Directory.Exists(job.OutputRoot))
        {
            _snackbar.ShowWarning("输出目录不存在。", "训练");
            return;
        }

        if (!_processLauncher.TryOpenFolder(job.OutputRoot))
        {
            _snackbar.ShowWarning("无法打开输出目录。", "训练");
        }
    }

    [RelayCommand]
    private void OpenLogFolderById(Guid jobId)
    {
        var job = RequireJob(jobId);
        if (job is null || string.IsNullOrWhiteSpace(job.LogFile) || !File.Exists(job.LogFile))
        {
            _snackbar.ShowWarning("日志文件不存在。", "训练");
            return;
        }

        var folder = Path.GetDirectoryName(job.LogFile);
        if (string.IsNullOrWhiteSpace(folder) || !_processLauncher.TryOpenFolder(folder))
        {
            _snackbar.ShowWarning("无法打开日志目录。", "训练");
            return;
        }
    }

    public bool CanDeleteJob(TrainingJobListItem job) => _workspace.CanDeleteJob(job);

    public bool CanStopJob(TrainingJobListItem job) => _workspace.CanStopJob(job);

    /// <summary>
    /// Release page work and the subscriptions that point at DI singletons, without stopping backend
    /// training. This is what page unload calls; it is paired with <see cref="Activate"/>.
    /// </summary>
    public void Deactivate() => _workspace.Deactivate();

    public void Activate() => _workspace.Activate();

    /// <summary>
    /// Full teardown, for when the view model is really discarded. Page unload must not call this:
    /// the page instance can be shown again, and disposing here races in-flight loads (see
    /// TrainingPage). Everything released only here — the child-view-model event subscriptions and
    /// the timer teardown — refers to objects inside this view model's own graph, so a discarded
    /// page collects them together; nothing singleton-facing is left subscribed because
    /// <see cref="Deactivate"/> already covered it.
    /// </summary>
    public void Dispose()
    {
        _workspace.CommandStatesChanged -= NotifyDeleteCommandsCanExecuteChanged;
        _workspace.Dispose();
    }

    private void NotifyDeleteCommandsCanExecuteChanged() => ConfirmDeleteJobCommand.NotifyCanExecuteChanged();

    private TrainingJobListItem? RequireJob(Guid jobId) =>
        _jobs.Jobs.FirstOrDefault(job => job.Id == jobId);
}
