using MediatR;

using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.ViewModels.Training;

namespace DeepVisionSharp.Shell.Services.Training;

/// <summary>
/// Start / stop / save-as-model command bodies for the training config surface.
/// Owned by <see cref="TrainingConfigViewModel"/>.
/// </summary>
public sealed class TrainingJobLifecycleActions
{
    private readonly ITrainingJobLifecycleHost _host;
    private readonly IMediator _mediator;
    private readonly ShellSnackbarNotifier _snackbar;

    public TrainingJobLifecycleActions(
        ITrainingJobLifecycleHost host,
        IMediator mediator,
        ShellSnackbarNotifier snackbar)
    {
        _host = host;
        _mediator = mediator;
        _snackbar = snackbar;
    }

    public async Task SaveAsModelAsync()
    {
        if (_host.Jobs.CurrentProjectId is null || _host.Jobs.SelectedJob is null)
        {
            return;
        }

        var result = await _mediator.Send(new SaveTrainedModelCommand(
            _host.Jobs.CurrentProjectId.Value,
            _host.Jobs.SelectedJob.Id,
            _host.ModelName,
            TargetModelId: null,
            _host.Monitor.SelectedCheckpointForSave));

        if (result.IsFailure || result.Value == Guid.Empty)
        {
            _snackbar.ShowError(result.Error ?? "保存模型失败", "训练");
            return;
        }

        _snackbar.ShowSuccess("训练模型已保存到模型库。", "训练");
    }

    public async Task StartTrainingAsync(bool resume = false)
    {
        if (_host.Jobs.CurrentProjectId is null || _host.Jobs.SelectedJob is null)
        {
            _snackbar.ShowWarning("请先选择或创建训练任务。", "训练");
            return;
        }

        if (resume)
        {
            if (!TrainingJobActionRules.CanResume(_host.Jobs.SelectedJob))
            {
                _snackbar.ShowWarning("当前任务不能续训：分类训练暂不支持续训。", "训练");
                return;
            }
        }

        if (!_host.IsDatasetReadyForSelectedModel)
        {
            _snackbar.ShowWarning(_host.GetDatasetBlockReason(), "训练");
            return;
        }

        var jobId = _host.Jobs.SelectedJob.Id;
        var operation = _host.Jobs.CapturePageOperation();
        var form = _host.BuildForm();

        // Non-blocking heads-up: the ResNet v2 stem keeps the first stage at full resolution, so a
        // large input/batch combination can exhaust the card even though the config is legal.
        var memoryRisk = TrainingResourceHeuristics.DescribeMemoryRisk(
            form.ModelType,
            form.NetworkInputWidth,
            form.NetworkInputHeight,
            form.BatchSize);
        if (memoryRisk is not null)
        {
            _snackbar.ShowWarning(memoryRisk, "训练");
        }

        _host.IsStartingTraining = true;
        _host.NotifyCommandStates();
        _host.Monitor.ClearJobMetrics(jobId);
        // Keep tracking so startup training lines are not dropped. Terminal toasts stay suppressed
        // until AllowTerminalStatusToasts after StartTrainingJobCommand returns.
        _host.Monitor.ResetLogForStart();
        _host.Monitor.AppendLogLine("正在启动训练：环境检测与数据集准备中…");
        // Pin before status-driven reloads so Preparing/Running refresh keeps this job selected.
        _host.Jobs.SelectJobById(jobId);
        // Switch to the run panel immediately: the command returns once the job is Preparing, and
        // the run continues on the background worker.
        _host.Monitor.EnterRunMode(TrainingRunSection.Logs);

        try
        {
            var result = await _mediator.Send(new StartTrainingJobCommand(
                operation.ProjectId!.Value,
                jobId,
                form,
                Resume: resume));

            if (!_host.Jobs.IsCurrent(operation)) return;
            if (!_host.Jobs.IsJobSelected(jobId))
            {
                // Keep the completed job's row current without taking over another job's detail.
                await _host.Jobs.ReloadJobsAsync(operation.Token, operation.SessionVersion);
                return;
            }

            if (result.IsFailure || result.Value is null)
            {
                var error = result.Error ?? "启动训练失败";
                // In-process cancellation returns a failure Result with an already-terminal Cancelled job.
                if (error.Contains("取消", StringComparison.Ordinal))
                {
                    _snackbar.ShowInfo(error, "训练");
                }
                else
                {
                    _snackbar.ShowError(error, "训练");
                }

                await _host.Jobs.ReloadJobsAsync(operation.Token, operation.SessionVersion);
                if (!_host.Jobs.IsCurrent(operation) || !_host.Jobs.IsJobSelected(jobId)) return;
                _host.Monitor.RefreshRunningState(_host.Jobs.SelectedJob);
                // Failure before/after Preparing: return to settings when the job is still Draft.
                if (_host.Jobs.SelectedJob?.Status == TrainingJobStatus.Draft)
                {
                    _host.Monitor.EnterSettingsMode(TrainingSettingsSection.Basic);
                }

                return;
            }

            foreach (var message in result.Value.InformationalMessages)
            {
                _host.Monitor.AppendLogLine(message);
            }

            // The run continues on the background worker: track its log now and let the status
            // event report completion, failure or cancellation.
            _host.Monitor.StartLogTracking(jobId, result.Value.LogFile, _host.Jobs.SelectedJob);
            _host.Monitor.EnterRunMode(TrainingRunSection.Logs);
            _snackbar.ShowSuccess(resume ? "已从 last.pt 继续训练。" : "训练已启动。", "训练");
            _host.Monitor.AllowTerminalStatusToasts();

            await _host.Jobs.ReloadJobsAsync(operation.Token, operation.SessionVersion);
            if (!_host.Jobs.IsCurrent(operation) || !_host.Jobs.IsJobSelected(jobId)) return;
            _host.Monitor.RefreshRunningState(_host.Jobs.SelectedJob);
        }
        catch (OperationCanceledException) when (!_host.Jobs.IsCurrent(operation))
        {
            // Only page work was cancelled; the training command deliberately has no page token.
        }
        finally
        {
            if (_host.Jobs.IsCurrent(operation))
            {
                _host.IsStartingTraining = false;
                if (_host.Jobs.IsJobSelected(jobId)) _host.Monitor.AllowTerminalStatusToasts();
                _host.NotifyCommandStates();
                _host.NotifyDetailState();
            }
        }
    }

    public async Task StopTrainingAsync()
    {
        if (_host.Jobs.SelectedJob is null)
        {
            return;
        }

        var result = await _mediator.Send(new StopTrainingJobCommand(_host.Jobs.SelectedJob.Id));
        if (result.IsFailure)
        {
            _snackbar.ShowError(result.Error ?? "停止训练失败", "训练");
        }
        else
        {
            _snackbar.ShowInfo("已请求停止训练。", "训练");
        }
    }

    public string GetStartBlockReason()
    {
        if (_host.IsStartingTraining)
        {
            return "正在启动训练：环境检测与数据集准备中…";
        }

        if (!_host.Jobs.HasProjectContext)
        {
            return "请先选择项目";
        }

        if (_host.Jobs.SelectedJob is null)
        {
            return "请先选择或创建训练任务";
        }

        // Run / terminal jobs: start prerequisites (dataset) are irrelevant.
        if (!_host.IsFormEditable || _host.Monitor.IsRunning
            || TrainingJobUi.IsActiveStatus(_host.Jobs.SelectedJob.Status))
        {
            return string.Empty;
        }

        if (!_host.IsDatasetReadyForSelectedModel)
        {
            return _host.GetDatasetBlockReason();
        }

        return "无法开始训练";
    }

    public string GetDatasetBlockReason()
    {
        if (_host.SelectedModelCapabilities?.Family != ModelFamily.Classification)
        {
            return $"模型类型 {ModelTypeNames.GetDisplayLabel(_host.SelectedModelType)} 尚未接入数据集配置";
        }

        if (!string.IsNullOrWhiteSpace(_host.Dataset.DatasetValidationMessage))
        {
            return _host.Dataset.DatasetValidationMessage;
        }

        if (_host.Dataset.EstimatedTrainSamples <= 0 || _host.Dataset.EstimatedValSamples <= 0)
        {
            return $"数据集划分无效：预估训练 {_host.Dataset.EstimatedTrainSamples} / 验证 {_host.Dataset.EstimatedValSamples}（两者均需 > 0；样本较少时请提高验证集比例或增加已标注样本）";
        }

        return "请完成数据集配置";
    }
}

/// <summary>Host surface for <see cref="TrainingJobLifecycleActions"/>.</summary>
public interface ITrainingJobLifecycleHost
{
    TrainingJobsViewModel Jobs { get; }

    TrainingMonitorViewModel Monitor { get; }

    TrainingDatasetViewModel Dataset { get; }

    string ModelName { get; }

    string SelectedModelType { get; }

    bool IsStartingTraining { get; set; }

    bool IsFormEditable { get; }

    bool IsDatasetReadyForSelectedModel { get; }

    ModelCapabilities? SelectedModelCapabilities { get; }

    TrainingJobForm BuildForm();

    string GetDatasetBlockReason();

    void NotifyCommandStates();

    void NotifyDetailState();
}

