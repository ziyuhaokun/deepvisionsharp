using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Shell.ViewModels.Training;

namespace DeepVisionSharp.Shell.Services.Training;

/// <summary>
/// Debounced draft-job form persistence. Owned by <see cref="TrainingConfigViewModel"/>.
/// </summary>
public sealed class TrainingDraftFormSaver
{
    private readonly ITrainingDraftFormSaverHost _host;
    private readonly IMediator _mediator;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ILogger _logger;
    private CancellationTokenSource? _draftFormSaveCts;
    private string? _lastDraftSaveErrorSnackbar;

    public TrainingDraftFormSaver(
        ITrainingDraftFormSaverHost host,
        IMediator mediator,
        ShellSnackbarNotifier snackbar,
        ILogger logger)
    {
        _host = host;
        _mediator = mediator;
        _snackbar = snackbar;
        _logger = logger;
    }

    public void CancelPending()
    {
        _draftFormSaveCts?.Cancel();
        _draftFormSaveCts?.Dispose();
        _draftFormSaveCts = null;
    }

    public void ScheduleSave()
    {
        if (_host.SuppressDraftFormSave || !_host.IsFormEditable || _host.Jobs.SelectedJob is null)
        {
            return;
        }

        CancelPending();
        _draftFormSaveCts = new CancellationTokenSource();
        var jobId = _host.Jobs.SelectedJob.Id;
        var token = _draftFormSaveCts.Token;
        ShellAsync.FireAndForget(
            () => SaveDebouncedAsync(jobId, token),
            _logger,
            "SaveDraftFormDebounced",
            ex => _snackbar.ShowError($"保存任务配置失败：{ex.Message}", "训练"));
    }

    private async Task SaveDebouncedAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await Task.Delay(400, cancellationToken);
        if (cancellationToken.IsCancellationRequested || _host.Jobs.SelectedJob?.Id != jobId)
        {
            return;
        }

        var result = await _mediator.Send(new UpdateTrainingJobCommand(jobId, _host.BuildForm()), cancellationToken);
        if (result.IsFailure)
        {
            var error = result.Error ?? "更新任务配置失败";
            _logger.LogWarning(
                "Draft training job update failed for {JobId}: {Error}",
                jobId,
                error);

            // Deduped snackbar so validation failures are visible without spamming on every keystroke.
            if (!string.Equals(_lastDraftSaveErrorSnackbar, error, StringComparison.Ordinal))
            {
                _lastDraftSaveErrorSnackbar = error;
                _snackbar.ShowError(error, "训练");
            }
        }
        else
        {
            _lastDraftSaveErrorSnackbar = null;
        }
    }
}

/// <summary>Host surface for <see cref="TrainingDraftFormSaver"/>.</summary>
public interface ITrainingDraftFormSaverHost
{
    TrainingJobsViewModel Jobs { get; }

    bool SuppressDraftFormSave { get; }

    bool IsFormEditable { get; }

    TrainingJobForm BuildForm();
}
