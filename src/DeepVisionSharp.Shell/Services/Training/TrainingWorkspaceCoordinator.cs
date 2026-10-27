using System.ComponentModel;
using MediatR;
using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.ViewModels.Training;

namespace DeepVisionSharp.Shell.Services.Training;

/// <summary>
/// Owns one training page's child graph, cross-panel synchronization and UI lifetime.
/// Deactivation releases page work and subscriptions, never the running training backend.
/// </summary>
public sealed class TrainingWorkspaceCoordinator : IDisposable
{
    private readonly ITrainingJobLifecycleService _lifecycle;
    private readonly ITrainingProcessHost _processHost;
    private readonly WorkspaceSessionService _workspaceSession;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger<TrainingWorkspaceCoordinator> _logger;
    private sealed record Activation(CancellationTokenSource Source, CancellationToken Token);

    private Activation? _activation;
    private bool _disposed;

    public TrainingWorkspaceCoordinator(
        IMediator mediator,
        WorkspaceSessionService workspaceSession,
        ITrainingJobLifecycleService lifecycle,
        ITrainingProcessHost processHost,
        ShellSnackbarNotifier snackbar,
        IAppDialogService dialogs,
        IProcessLauncher processLauncher,
        IClipboardService clipboard,
        IUiDispatcher uiDispatcher,
        DeepVisionLaunchOptions launchOptions,
        ILoggerFactory loggerFactory,
        TrainingMetricsPoller metricsPoller,
        TrainingPreviewPoller previewPoller,
        TrainingResourcePoller resourcePoller)
    {
        _lifecycle = lifecycle;
        _processHost = processHost;
        _workspaceSession = workspaceSession;
        _uiDispatcher = uiDispatcher;
        _snackbar = snackbar;
        _logger = loggerFactory.CreateLogger<TrainingWorkspaceCoordinator>();

        JobList = new TrainingJobsViewModel(mediator, workspaceSession, snackbar, uiDispatcher,
            launchOptions, loggerFactory.CreateLogger<TrainingJobsViewModel>());
        Log = new TrainingLogViewModel(processHost, snackbar, clipboard, uiDispatcher,
            loggerFactory.CreateLogger<TrainingLogViewModel>());
        Monitor = new TrainingMonitorViewModel(JobList, processHost, processLauncher, snackbar, Log,
            metricsPoller, previewPoller, resourcePoller, clipboard, uiDispatcher);
        Config = new TrainingConfigViewModel(mediator, JobList, Monitor, snackbar,
            dialogs, loggerFactory.CreateLogger<TrainingConfigViewModel>());

        Monitor.PropertyChanged += OnChildPropertyChanged;
        JobList.PropertyChanged += OnChildPropertyChanged;
        JobList.SelectedJobChanged += OnSelectedJobChanged;
        JobList.JobCreated += OnJobCreated;
        JobList.JobDeleted += Monitor.ClearJobMetrics;
        JobList.GalleriesReloaded += OnGalleriesReloaded;
        Activate();
    }

    public TrainingJobsViewModel JobList { get; }
    public TrainingConfigViewModel Config { get; }
    public TrainingMonitorViewModel Monitor { get; }
    public TrainingLogViewModel Log { get; }

    /// <summary>UI command availability changed after selection, status or running-state updates.</summary>
    public event Action? CommandStatesChanged;

    public bool CanDeleteJob(TrainingJobListItem job) => TrainingJobActionRules.CanDelete(job, _processHost);
    public bool CanStopJob(TrainingJobListItem job) => TrainingJobActionRules.CanStop(job, _processHost);

    public void Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activation is not null)
        {
            return;
        }

        var source = new CancellationTokenSource();
        _activation = new Activation(source, source.Token);
        _lifecycle.JobStatusChanged += OnJobStatusChanged;
        Log.Activate();
    }

    public void Deactivate()
    {
        var activation = _activation;
        if (activation is null)
        {
            return;
        }

        _activation = null;
        _lifecycle.JobStatusChanged -= OnJobStatusChanged;
        activation.Source.Cancel();
        activation.Source.Dispose();
        try
        {
            JobList.CancelPendingPageWork();
            Config.CancelPendingDraftFormSave();
            Config.IsStartingTraining = false;
            Monitor.StopAllTabPolling();
        }
        finally
        {
            JobList.StopElapsedTimer();
            Log.StopTracking();
            Log.Deactivate();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Deactivate();
        _disposed = true;
        Monitor.PropertyChanged -= OnChildPropertyChanged;
        JobList.PropertyChanged -= OnChildPropertyChanged;
        JobList.SelectedJobChanged -= OnSelectedJobChanged;
        JobList.JobCreated -= OnJobCreated;
        JobList.JobDeleted -= Monitor.ClearJobMetrics;
        JobList.GalleriesReloaded -= OnGalleriesReloaded;
        Monitor.Dispose();
        Log.Dispose();
    }

    private void OnJobCreated(Guid _) => Monitor.EnterSettingsMode(TrainingSettingsSection.Basic);

    private void OnGalleriesReloaded()
    {
        Config.Dataset.AttachGalleryChangeHandlers();
        Config.NotifyCommandStates();
    }

    private void OnSelectedJobChanged(TrainingJobListItem? job)
    {
        var activation = _activation;
        if (activation is null)
        {
            return;
        }

        try
        {
            Config.HandleSelectedJobChanged(job, activation.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Training config failed to bind selected job {JobId}", job?.Id);
            _snackbar.ShowError($"加载任务配置失败：{ex.Message}", "训练");
        }

        // Isolate the form's property storm so monitor refresh still runs on a form failure.
        try
        {
            Monitor.HandleSelectedJobChanged(job);
            Config.NotifyCommandStates();
            NotifyCommandStatesChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Training monitor failed to bind selected job {JobId}", job?.Id);
            _snackbar.ShowError($"加载任务监控状态失败：{ex.Message}", "训练");
        }
    }

    private void OnJobStatusChanged(Guid jobId, TrainingJobStatus status, string? bestModelPath)
    {
        var activation = _activation;
        if (activation is null)
        {
            return;
        }

        var token = activation.Token;
        var sessionVersion = _workspaceSession.Current.Version;
        // Check inside the dispatcher callback too: a queued event may outlive page unload/reload.
        _uiDispatcher.Post(() => ShellAsync.FireAndForget(
            () => ApplyJobStatusAsync(jobId, status, bestModelPath, sessionVersion, token),
            _logger,
            "HandleJobStatusChanged",
            ex => _snackbar.ShowError($"更新训练任务状态失败：{ex.Message}", "训练")));
    }

    private async Task ApplyJobStatusAsync(Guid jobId, TrainingJobStatus status, string? bestModelPath,
        long sessionVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_workspaceSession.IsCurrent(sessionVersion))
        {
            return;
        }

        Monitor.HandleJobStatusChanged(jobId, status, bestModelPath);
        await JobList.ReloadJobsAsync(cancellationToken, sessionVersion);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_workspaceSession.IsCurrent(sessionVersion))
        {
            return;
        }

        Config.NotifyCommandStates();
        Config.NotifyDetailState();
        NotifyCommandStatesChanged();
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender == Monitor && e.PropertyName == nameof(TrainingMonitorViewModel.IsRunning))
        {
            Config.NotifyCommandStates();
            NotifyCommandStatesChanged();
        }
    }

    private void NotifyCommandStatesChanged()
    {
        JobList.NotifyDeleteCanExecuteChanged();
        CommandStatesChanged?.Invoke();
    }
}
