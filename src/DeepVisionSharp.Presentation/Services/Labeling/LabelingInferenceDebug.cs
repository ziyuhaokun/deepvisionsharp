using System.IO;
using System.Text;

using MediatR;

using Microsoft.Extensions.Logging;

using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.ValueObjects;
using DeepVisionSharp.Presentation.Ports;
using DeepVisionSharp.Presentation.Services;
using DeepVisionSharp.Presentation.Services.Dialogs;
using DeepVisionSharp.Presentation.ViewModels.Labeling;

namespace DeepVisionSharp.Presentation.Services.Labeling;

/// <summary>
/// Dry-run inference diagnostics (no annotation mutation) for the labeling page.
/// Owned by <see cref="LabelingInferenceViewModel"/>.
/// </summary>
public sealed class LabelingInferenceDebug
{
    private readonly ILabelingInferenceHost _host;
    private readonly IMediator _mediator;
    private readonly IAppDialogService _appDialogService;
    private readonly ShellSnackbarNotifier _snackbar;
    private readonly IClipboardService _clipboard;
    private readonly ILogger _logger;
    private readonly LabelingInferenceModelCatalog _catalog;

    public LabelingInferenceDebug(
        ILabelingInferenceHost host,
        IMediator mediator,
        IAppDialogService appDialogService,
        ShellSnackbarNotifier snackbar,
        IClipboardService clipboard,
        ILogger logger,
        LabelingInferenceModelCatalog catalog)
    {
        _host = host;
        _mediator = mediator;
        _appDialogService = appDialogService;
        _snackbar = snackbar;
        _clipboard = clipboard;
        _logger = logger;
        _catalog = catalog;
    }

    public async Task RunAsync()
    {
        await _catalog.LoadAsync();
        if (!_host.TryValidateInferenceDraft())
        {
            _snackbar.ShowWarning(
                string.IsNullOrWhiteSpace(_host.InferenceDialogErrorMessage)
                    ? "调试推理前请先配置有效模型与参数。"
                    : _host.InferenceDialogErrorMessage,
                "推理调试");
            return;
        }

        var context = _host.Context;
        var model = _host.SelectedInferenceModel;
        if (context.Gallery is null
            || model is null
            || string.IsNullOrWhiteSpace(context.ActiveImagePath)
            || !File.Exists(context.ActiveImagePath))
        {
            _snackbar.ShowWarning("请先选择模型并打开有效样本图像。", "推理调试");
            return;
        }

        if (model is not { CheckpointPath: { Length: > 0 } checkpointPath }
            || !File.Exists(checkpointPath))
        {
            _snackbar.ShowWarning("所选模型文件不存在。", "推理调试");
            return;
        }

        var config = _host.BuildInferenceConfig();
        if (!_host.IsInferenceConfigValid)
        {
            _snackbar.ShowWarning("推理参数无效，请检查阈值与批次设置。", "推理调试");
            return;
        }

        // Freeze the sample: the report must describe the image that was actually inferred.
        var target = LabelingSampleIdentity.Capture(context);
        context.IsInferring = true;
        _host.NotifyInferenceCommandsCanExecute();
        try
        {
            var result = await _mediator.Send(new RunSingleInferenceCommand(
                checkpointPath,
                target.ImagePath,
                config,
                model.ModelType));

            if (result.IsFailure || result.Value is null || !result.Value.Success)
            {
                var error = result.Error ?? result.Value?.Error ?? "Unknown inference error";
                _host.InferenceDebugHeadline = "推理失败";
                _host.InferenceDebugReport = BuildFailureDebugReport(config, checkpointPath, target, error);
                _logger.LogWarning(
                    "Inference debug failed for sample {SampleId}, image {ImageName}, model {ModelName}. Error={Error}",
                    target.SampleId,
                    Path.GetFileName(target.ImagePath),
                    model.Name,
                    error);
            }
            else
            {
                var analysis = AnalyzeInferenceResult(result.Value, config, checkpointPath, target);
                _host.InferenceDebugHeadline = analysis.Headline;
                _host.InferenceDebugReport = analysis.Report;
                _logger.LogInformation(
                    "Inference debug completed for sample {SampleId}, image {ImageName}: {Headline}",
                    target.SampleId,
                    Path.GetFileName(target.ImagePath),
                    analysis.Headline);
            }

            var dialogResult = await _appDialogService.ShowAsync(
                AppDialogKind.InferenceDebug,
                _host.DialogDataContext,
                new DialogOptions(
                    Title: "推理调试报告",
                    PrimaryButtonText: "复制报告",
                    CloseButtonText: "关闭",
                    DefaultButton: AppDialogDefaultButton.Close));

            if (dialogResult == AppDialogResult.Primary)
            {
                TryCopyDebugReportToClipboard();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inference debug failed for sample {SampleId}, image {ImageName}", context.ActiveSampleId, Path.GetFileName(context.ActiveImagePath));
            _snackbar.ShowError($"调试推理失败：{ex.Message}", "推理调试");
        }
        finally
        {
            context.IsInferring = false;
            _host.NotifyInferenceCommandsCanExecute();
        }
    }

    private void TryCopyDebugReportToClipboard()
    {
        try
        {
            var textToCopy = string.IsNullOrWhiteSpace(_host.InferenceDebugReport)
                ? _host.InferenceDebugHeadline
                : _host.InferenceDebugReport;
            if (string.IsNullOrWhiteSpace(textToCopy))
            {
                _snackbar.ShowWarning("没有可复制的调试内容。", "推理调试");
                return;
            }

            _clipboard.SetText(textToCopy);
            _snackbar.ShowSuccess("调试报告已复制到剪贴板。", "推理调试");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy inference debug report");
            _snackbar.ShowError("复制调试报告失败。", "推理调试");
        }
    }

    private string BuildFailureDebugReport(
        InferenceConfig config,
        string checkpointPath,
        LabelingSampleIdentity target,
        string error)
    {
        var sb = new StringBuilder();
        AppendDebugHeader(sb, config, checkpointPath, target);
        sb.AppendLine();
        sb.AppendLine("=== 结果 ===");
        sb.AppendLine("状态: 失败");
        sb.AppendLine($"错误: {error}");
        return sb.ToString();
    }

    private InferenceDebugAnalysis AnalyzeInferenceResult(
        InferenceResult inference,
        InferenceConfig config,
        string checkpointPath,
        LabelingSampleIdentity target)
    {
        var projectLabels = _host.Labels.LabelOptions
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var projectLabelSet = new HashSet<string>(projectLabels, StringComparer.OrdinalIgnoreCase);

        if (!_host.IsClassificationProject)
        {
            return AnalyzeDetectionResult(inference, projectLabels, projectLabelSet, config, checkpointPath, target);
        }

        var matched = 0;
        var skippedUnknownClass = 0;
        var skippedBackground = 0;
        var detectionClasses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var detailLines = new List<string>();
        var maxDetail = 50;

        for (var index = 0; index < inference.Detections.Count; index++)
        {
            var detection = inference.Detections[index];
            var className = string.IsNullOrWhiteSpace(detection.ClassName) ? detection.Label : detection.ClassName;
            className = className?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(className))
            {
                detectionClasses[className] = detectionClasses.TryGetValue(className, out var count) ? count + 1 : 1;
            }

            string status;
            if (string.Equals(className, "__background__", StringComparison.OrdinalIgnoreCase)
                || string.Equals(className, "background", StringComparison.OrdinalIgnoreCase))
            {
                skippedBackground++;
                status = "SKIP background";
            }
            else if (!projectLabelSet.Contains(className))
            {
                skippedUnknownClass++;
                status = "SKIP class-not-in-project-labels";
            }
            else
            {
                matched++;
                status = "MATCH";
            }

            if (index < maxDetail)
            {
                detailLines.Add(
                    $"[{index + 1}] {status} class=\"{className}\" conf={detection.Confidence:F4}");
            }
        }

        if (inference.Detections.Count > maxDetail)
        {
            detailLines.Add($"... 另有 {inference.Detections.Count - maxDetail} 条未展开");
        }

        var onlyInDetections = detectionClasses.Keys
            .Where(name => !projectLabelSet.Contains(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var onlyInProject = projectLabels
            .Where(name => !detectionClasses.ContainsKey(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var engineCount = inference.Detections.Count;
        var headline = $"引擎返回 {engineCount} · 可写入标注 {matched} · 类名跳过 {skippedUnknownClass}";

        var sb = new StringBuilder();
        AppendDebugHeader(sb, config, checkpointPath, target);
        sb.AppendLine();
        sb.AppendLine("=== 引擎结果 ===");
        sb.AppendLine("状态: 成功");
        sb.AppendLine($"Performance: {inference.Performance?.GetSummary() ?? "(无)"}");
        if (inference.Performance is { } perf)
        {
            sb.AppendLine(
                $"耗时明细 ms: total={perf.TotalMilliseconds:F1} read={perf.ReadImageMilliseconds:F1} " +
                $"infer={perf.InferenceMilliseconds:F1}");
            sb.AppendLine(
                $"图像: {perf.ImageWidth}x{perf.ImageHeight} · " +
                $"device={perf.Device} · cuda={perf.CudaAvailable} · gpu={perf.GpuDeviceName ?? "-"}");
            sb.AppendLine($"Performance.ResultCount={perf.ResultCount}");
        }



        sb.AppendLine();
        sb.AppendLine("=== 标注应用模拟（未实际写入）===");
        sb.AppendLine($"引擎 detections: {engineCount}");
        sb.AppendLine($"可匹配项目标签: {matched}");
        sb.AppendLine($"跳过-类名不在项目标签: {skippedUnknownClass}");
        sb.AppendLine($"跳过-background: {skippedBackground}");

        sb.AppendLine();
        sb.AppendLine("=== 项目标签 ===");
        sb.AppendLine(projectLabels.Count == 0 ? "(无)" : string.Join(", ", projectLabels));

        sb.AppendLine();
        sb.AppendLine("=== 引擎返回类名统计 ===");
        if (detectionClasses.Count == 0)
        {
            sb.AppendLine("(无)");
        }
        else
        {
            foreach (var pair in detectionClasses.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
            {
                var inProject = projectLabelSet.Contains(pair.Key) ? "match" : "NO-MATCH";
                sb.AppendLine($"  {pair.Key}: {pair.Value} ({inProject})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== 集合差 ===");
        sb.AppendLine(
            "仅引擎出现: " + (onlyInDetections.Count == 0 ? "(无)" : string.Join(", ", onlyInDetections)));
        sb.AppendLine(
            "仅项目标签: " + (onlyInProject.Count == 0 ? "(无)" : string.Join(", ", onlyInProject)));

        sb.AppendLine();
        sb.AppendLine($"=== 类别明细（最多 {maxDetail} 条）===");
        if (detailLines.Count == 0)
        {
            sb.AppendLine("(无类别分数)");
        }
        else
        {
            foreach (var line in detailLines)
            {
                sb.AppendLine(line);
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== 解读建议 ===");
        if (engineCount == 0)
        {
            sb.AppendLine("- 引擎未返回类别：尝试降低置信度阈值，或检查模型/图像是否匹配。");
        }
        else if (matched == 0 && skippedUnknownClass > 0)
        {
            sb.AppendLine("- 引擎有结果但类名与项目标签不一致：请统一类别命名或在项目中补齐标签。");
        }
        else if (matched < engineCount)
        {
            sb.AppendLine("- 部分结果被过滤：优先核对类名是否与项目标签一致。");
        }
        else if (engineCount == 1)
        {
            sb.AppendLine("- 引擎只返回 1 个类别：分类模型按 top-1 写回，这是预期结果。");
        }
        else
        {
            sb.AppendLine("- 引擎与标签匹配正常；若页面仍无类别，请确认是否查看的是本次结果样本。");
        }

        return new InferenceDebugAnalysis(headline, sb.ToString());
    }

    private InferenceDebugAnalysis AnalyzeDetectionResult(
        InferenceResult inference,
        IReadOnlyList<string> projectLabels,
        HashSet<string> projectLabelSet,
        InferenceConfig config,
        string checkpointPath,
        LabelingSampleIdentity target)
    {
        var matched = 0;
        var skippedUnknownClass = 0;
        var boxClasses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var detailLines = new List<string>();
        const int maxDetail = 50;

        for (var index = 0; index < inference.Boxes.Count; index++)
        {
            var box = inference.Boxes[index];
            var className = string.IsNullOrWhiteSpace(box.ClassName) ? box.Label : box.ClassName;
            className = className?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(className))
            {
                boxClasses[className] = boxClasses.TryGetValue(className, out var count) ? count + 1 : 1;
            }

            var status = projectLabelSet.Contains(className) ? "MATCH" : "SKIP class-not-in-project-labels";
            if (status == "MATCH")
            {
                matched++;
            }
            else
            {
                skippedUnknownClass++;
            }

            if (index < maxDetail)
            {
                detailLines.Add(
                    $"[{index + 1}] {status} class=\"{className}\" conf={box.Confidence:F4} box=({box.X:F1},{box.Y:F1},{box.Width:F1},{box.Height:F1})");
            }
        }

        if (inference.Boxes.Count > maxDetail)
        {
            detailLines.Add($"... 另有 {inference.Boxes.Count - maxDetail} 条未展开");
        }

        var boxCount = inference.Boxes.Count;
        var headline = $"检测返回 {boxCount} 框 · 可写入 {matched} · 跳过未知类 {skippedUnknownClass}";

        var sb = new StringBuilder();
        AppendDebugHeader(sb, config, checkpointPath, target);
        sb.AppendLine();
        sb.AppendLine("=== 引擎结果 ===");
        sb.AppendLine("状态: 成功");
        sb.AppendLine($"Performance: {inference.Performance?.GetSummary() ?? "(无)"}");
        if (inference.Performance is { } perf)
        {
            sb.AppendLine(
                $"耗时明细 ms: total={perf.TotalMilliseconds:F1} read={perf.ReadImageMilliseconds:F1} " +
                $"infer={perf.InferenceMilliseconds:F1}");
            sb.AppendLine(
                $"图像: {perf.ImageWidth}x{perf.ImageHeight} · " +
                $"device={perf.Device} · cuda={perf.CudaAvailable} · gpu={perf.GpuDeviceName ?? "-"}");
        }

        sb.AppendLine($"检测框数: {boxCount}, 可匹配项目标签: {matched}, 未知类跳过: {skippedUnknownClass}");
        sb.AppendLine($"项目标签: {(projectLabels.Count == 0 ? "(无)" : string.Join(", ", projectLabels))}");
        sb.AppendLine();
        sb.AppendLine("=== 引擎返回类名统计 ===");
        if (boxClasses.Count == 0)
        {
            sb.AppendLine("(无)");
        }
        else
        {
            foreach (var pair in boxClasses.OrderByDescending(item => item.Value).ThenBy(item => item.Key))
            {
                var inProject = projectLabelSet.Contains(pair.Key) ? "match" : "NO-MATCH";
                sb.AppendLine($"  {pair.Key}: {pair.Value} ({inProject})");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"=== 框明细（最多 {maxDetail} 条）===");
        if (detailLines.Count == 0)
        {
            sb.AppendLine("(无预测框)");
        }
        else
        {
            foreach (var line in detailLines)
            {
                sb.AppendLine(line);
            }
        }

        sb.AppendLine();
        sb.AppendLine("=== 解读建议 ===");
        if (boxCount == 0)
        {
            sb.AppendLine("- 引擎未检出目标框：尝试降低置信度阈值（如 0.25 → 0.15），或检查模型与图像场景是否匹配。");
        }
        else if (matched == 0 && skippedUnknownClass > 0)
        {
            sb.AppendLine("- 检出目标框但类名不在当前项目标签中：请在项目中补充对应标签名称。");
        }
        else if (matched < boxCount)
        {
            sb.AppendLine("- 部分检测框因类名不在项目标签中被过滤：核对上方的 NO-MATCH 类名。");
        }
        else
        {
            sb.AppendLine("- 检测框与项目标签全部匹配，自动推理可直接合并写回。");
        }

        return new InferenceDebugAnalysis(headline, sb.ToString());
    }

    private void AppendDebugHeader(
        StringBuilder sb,
        InferenceConfig config,
        string checkpointPath,
        LabelingSampleIdentity target)
    {
        var model = _host.SelectedInferenceModel;
        var isClassification = _host.IsClassificationProject;
        sb.AppendLine("=== 推理调试报告 ===");
        sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"工作流: {(isClassification ? "Classification" : "Detection")}");
        sb.AppendLine($"任务: {(isClassification ? "classify" : "detect")}");
        sb.AppendLine($"项目Id: {target.ProjectId}");
        sb.AppendLine($"图库Id: {target.GalleryId}");
        sb.AppendLine($"样本Id: {target.SampleId}");
        sb.AppendLine($"图像: {target.ImagePath}");
        sb.AppendLine($"模型: {model?.Name ?? "-"} ({(model is null ? "-" : ModelTypeNames.GetDisplayLabel(model.ModelType))})");
        sb.AppendLine($"模型Id: {model?.Id}");
        sb.AppendLine($"权重: {checkpointPath}");
        sb.AppendLine($"空间策略: {model?.SpatialPolicySummary ?? "-"}");
        sb.AppendLine($"{(isClassification ? "分类输入尺寸" : "检测输入尺寸")}: {model?.InputSize?.ToString() ?? "-"}");

        sb.AppendLine(
            $"请求参数: conf={config.ConfidenceThreshold:F3}, iou={LabelingSampleIdentity.FormatNmsIou(config)}, maxDet={config.MaxDetections}, batch={config.BatchSize}");
        sb.AppendLine(
            $"当前 UI 参数: conf={_host.InferenceConfidence:F3}, iou={_host.InferenceNmsIou:F2}, maxDet={_host.InferenceMaxDetections}, batch={_host.InferenceBatchSize}");
    }

    private sealed record InferenceDebugAnalysis(string Headline, string Report);
}
