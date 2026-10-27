using System.IO;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Shell.Services;
using DeepVisionSharp.Shell.Services.Labeling.WriteBack;
using DeepVisionSharp.Shell.Services.Tasks;
using DeepVisionSharp.Shell.ViewModels;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling;

/// <summary>
/// Executes auto-inference and routes results through the write-back registry.
/// Owned by <see cref="LabelingInferenceViewModel"/>.
/// </summary>
public sealed class LabelingInferenceRunner
{
    private readonly ILabelingInferenceHost _host;
    private readonly IMediator _mediator;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ShellTaskCenterViewModel _taskCenter;
    private readonly ILogger _logger;

    public LabelingInferenceRunner(
        ILabelingInferenceHost host,
        IMediator mediator,
        ShellSnackbarNotifier snackbar,
        ShellTaskCenterViewModel taskCenter,
        ILogger logger)
    {
        _host = host;
        _mediator = mediator;
        _snackbar = snackbar;
        _taskCenter = taskCenter;
        _logger = logger;
    }

    public async Task RunAsync()
    {
        var context = _host.Context;
        var model = _host.SelectedInferenceModel;
        if (context.Gallery is null
            || model is null
            || string.IsNullOrWhiteSpace(context.ActiveImagePath)
            || !File.Exists(context.ActiveImagePath))
        {
            _snackbar.ShowWarning("请先选择模型并打开有效样本图像。", "推理");
            return;
        }

        if (model is not { CheckpointPath: { Length: > 0 } checkpointPath }
            || !File.Exists(checkpointPath))
        {
            _snackbar.ShowWarning("所选模型文件不存在。", "推理");
            return;
        }

        var config = _host.BuildInferenceConfig();
        if (!_host.IsInferenceConfigValid)
        {
            _snackbar.ShowWarning("推理参数无效，请检查阈值与批次设置。", "推理");
            return;
        }

        var sampleName = Path.GetFileName(context.ActiveImagePath);
        var taskTitle = $"推理「{model.Name}」";
        var taskId = _taskCenter.StartTask(BackgroundTaskKind.Inference, taskTitle);
        _taskCenter.AppendLog(taskId, $"模型：{model.Name} ({ModelTypeNames.GetDisplayLabel(model.ModelType)})");
        _taskCenter.AppendLog(taskId, $"样本：{sampleName}");
        _taskCenter.AppendLog(
            taskId,
            $"参数：device={config.Device} conf={config.ConfidenceThreshold:F2} max_det={config.MaxDetections} batch={config.BatchSize}");
        _taskCenter.AppendLog(taskId, "正在加载模型并推理…");

        context.IsInferring = true;
        _host.NotifyInferenceCommandsCanExecute();

        var success = false;
        string? error = null;
        try
        {
            var result = await _mediator.Send(new RunSingleInferenceCommand(
                checkpointPath,
                context.ActiveImagePath,
                config,
                model.ModelType));

            if (result.IsFailure || result.Value is null || !result.Value.Success)
            {
                error = result.Error ?? result.Value?.Error ?? "Unknown inference error";
                _logger.LogError(
                    "Auto inference failed for project {ProjectId}, gallery {GalleryId}, sample {SampleId}, image {ImagePath}, model {ModelId}:{ModelName} ({ModelType}), conf {Confidence}, maxDetections {MaxDetections}, batchSize {BatchSize}, spatialPolicy {SpatialPolicy}. Error={Error}",
                    context.Gallery?.ProjectId,
                    context.Gallery?.Id,
                    context.ActiveSampleId,
                    context.ActiveImagePath,
                    model.Id,
                    model.Name,
                    model.ModelType,
                    config.ConfidenceThreshold,
                    config.MaxDetections,
                    config.BatchSize,
                    model.SpatialPolicySummary,
                    error);
                _snackbar.ShowError(error, "推理");
                return;
            }

            var inference = result.Value;
            var detectionCount = inference.Detections?.Count ?? 0;
            var elapsed = inference.Performance?.TotalMilliseconds;
            _taskCenter.AppendLog(
                taskId,
                elapsed is > 0
                    ? $"引擎返回 {detectionCount} 条结果 · {elapsed:F0} ms"
                    : $"引擎返回 {detectionCount} 条结果");

            var profileResult = _host.TryResolveSelectedProfile();
            if (profileResult.IsFailure || profileResult.Value is null)
            {
                error = profileResult.Error ?? "项目与模型不兼容，无法写回推理结果。";
                _snackbar.ShowError(error, "推理");
                return;
            }

            if (!_host.WriteBackRegistry.TryResolve(profileResult.Value.WriteBackKind, out var writeBack))
            {
                error = $"未注册写回策略：{profileResult.Value.WriteBackKind}（不支持的自动推理写回路径）";
                _snackbar.ShowError(error, "推理");
                return;
            }

            _taskCenter.AppendLog(taskId, $"写回策略：{profileResult.Value.WriteBackKind}");
            await writeBack.WriteBackAsync(new LabelingWriteBackContext
            {
                Inference = inference,
                Profile = profileResult.Value,
                Workspace = context,
                Editor = _host.Editor,
                Labels = _host.Labels,
                Model = model,
                Logger = _logger,
                Snackbar = _snackbar
            });

            success = true;
            _taskCenter.AppendLog(taskId, "推理完成，结果已写回当前样本。");
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Auto inference failed for {ImagePath}", context.ActiveImagePath);
            _snackbar.ShowError($"推理失败：{ex.Message}", "推理");
        }
        finally
        {
            _taskCenter.CompleteTask(taskId, success, error);
            context.IsInferring = false;
            _host.NotifyInferenceCommandsCanExecute();
        }
    }
}
