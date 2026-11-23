using System.IO;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Labeling.WriteBack;
using DeepVisionSharp.Presentation.Services.Tasks;
using DeepVisionSharp.Presentation.ViewModels;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Presentation.Services.Labeling;

/// <summary>
/// Executes auto-inference and routes results through the write-back registry.
/// Owned by <see cref="LabelingInferenceViewModel"/>.
/// </summary>
/// <remarks>
/// The target sample is pinned before the engine runs. A result is written back only when the
/// editor still shows that sample, so navigating away mid-run never moves boxes to another image.
/// </remarks>
public sealed class LabelingInferenceRunner
{
    private readonly ILabelingInferenceHost _host;
    private readonly IMediator _mediator;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly ShellTaskCenterViewModel _taskCenter;
    private readonly ILogger _logger;
    private CancellationTokenSource? _activeRunCts;

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

    /// <summary>Cancels the in-flight run; its result is dropped instead of written back.</summary>
    public void Cancel()
    {
        try
        {
            Interlocked.Exchange(ref _activeRunCts, null)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
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

        var target = LabelingSampleIdentity.Capture(context);
        var sampleName = Path.GetFileName(target.ImagePath);
        var taskTitle = $"推理「{model.Name}」";
        var taskId = _taskCenter.StartTask(BackgroundTaskKind.Inference, taskTitle);
        _taskCenter.AppendLog(taskId, $"模型：{model.Name} ({ModelTypeNames.GetDisplayLabel(model.ModelType)})");
        _taskCenter.AppendLog(taskId, $"样本：{sampleName}");
        _taskCenter.AppendLog(
            taskId,
            $"参数：device={config.Device} conf={config.ConfidenceThreshold:F2} iou={LabelingSampleIdentity.FormatNmsIou(config)} max_det={config.MaxDetections} batch={config.BatchSize}");
        _taskCenter.AppendLog(taskId, "正在加载模型并推理…");

        var cts = new CancellationTokenSource();
        Interlocked.Exchange(ref _activeRunCts, cts)?.Cancel();

        context.IsInferring = true;
        _host.NotifyInferenceCommandsCanExecute();

        var success = false;
        string? error = null;
        try
        {
            var result = await _mediator.Send(
                new RunSingleInferenceCommand(checkpointPath, target.ImagePath, config, model.ModelType),
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                error = "推理已取消，结果未写回。";
                _taskCenter.AppendLog(taskId, error);
                return;
            }

            if (result.IsFailure || result.Value is null || !result.Value.Success)
            {
                error = result.Error ?? result.Value?.Error ?? "Unknown inference error";
                _logger.LogError(
                    "Auto inference failed for project {ProjectId}, gallery {GalleryId}, sample {SampleId}, image {ImagePath}, model {ModelId}:{ModelName} ({ModelType}), conf {Confidence}, nmsIou {NmsIou}, maxDetections {MaxDetections}, batchSize {BatchSize}, spatialPolicy {SpatialPolicy}. Error={Error}",
                    target.ProjectId,
                    target.GalleryId,
                    target.SampleId,
                    target.ImagePath,
                    model.Id,
                    model.Name,
                    model.ModelType,
                    config.ConfidenceThreshold,
                    config.NmsIouThreshold,
                    config.MaxDetections,
                    config.BatchSize,
                    model.SpatialPolicySummary,
                    error);
                _snackbar.ShowError(error, "推理");
                return;
            }

            var inference = result.Value;
            var resultCount = ModelTypeNames.IsDetectionModelType(model.ModelType)
                ? inference.Boxes.Count
                : inference.Detections?.Count ?? 0;
            var elapsed = inference.Performance?.TotalMilliseconds;
            _taskCenter.AppendLog(
                taskId,
                elapsed is > 0
                    ? $"引擎返回 {resultCount} 条结果 · {elapsed:F0} ms"
                    : $"引擎返回 {resultCount} 条结果");

            if (!target.Matches(_host.Context))
            {
                error = "推理期间已切换样本，结果未写回，以免写到其他图片上。";
                _taskCenter.AppendLog(taskId, error);
                _snackbar.ShowWarning(error, "推理");
                return;
            }

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
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            error = "推理已取消，结果未写回。";
            _taskCenter.AppendLog(taskId, error);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex, "Auto inference failed for {ImagePath}", target.ImagePath);
            _snackbar.ShowError($"推理失败：{ex.Message}", "推理");
        }
        finally
        {
            Interlocked.CompareExchange(ref _activeRunCts, null, cts);
            cts.Dispose();
            _taskCenter.CompleteTask(taskId, success, error);
            context.IsInferring = false;
            _host.NotifyInferenceCommandsCanExecute();
        }
    }
}
