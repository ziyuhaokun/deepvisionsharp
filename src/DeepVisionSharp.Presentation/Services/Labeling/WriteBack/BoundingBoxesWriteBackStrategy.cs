using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Presentation.Services.Labeling.WriteBack;

/// <summary>
/// Object detection write-back: merges predicted boxes into current sample annotations using
/// append mode (existing boxes preserved, duplicate same-class predictions with IoU > 0.7 skipped).
/// </summary>
internal sealed class BoundingBoxesWriteBackStrategy : ILabelingInferenceWriteBackStrategy
{
    public InferenceWriteBackKind Kind => InferenceWriteBackKind.BoundingBoxes;

    public async Task WriteBackAsync(LabelingWriteBackContext context, CancellationToken cancellationToken = default)
    {
        if (context.Inference.Boxes.Count == 0)
        {
            context.Snackbar.ShowInfo("未检测到高于阈值的目标框，当前样本标注保持不变。", "推理");
            return;
        }

        var imageWidth = context.Workspace.ImageWidth > 0
            ? context.Workspace.ImageWidth
            : context.Inference.ImageWidth;
        var imageHeight = context.Workspace.ImageHeight > 0
            ? context.Workspace.ImageHeight
            : context.Inference.ImageHeight;

        var existing = new AnnotationDocument(
            imageWidth,
            imageHeight,
            context.Editor.ImageClass,
            context.Editor.Boxes.Select(box => box.ToShape()).ToArray(),
            context.Editor.IsReviewed);
        var knownLabels = context.Labels.LabelOptions
            .Select(item => item.Name)
            .ToArray();

        var merged = DetectionAnnotationMerger.Merge(
            existing,
            context.Inference.Boxes,
            knownLabels,
            append: true,
            imageWidth,
            imageHeight);
        if (merged.IsFailure)
        {
            context.Snackbar.ShowWarning(merged.Error ?? "无法合并预测框。", "推理");
            return;
        }

        if (merged.Value is null)
        {
            context.Snackbar.ShowWarning("预测框未命中当前项目类别或尺寸无效，未写回。", "推理");
            return;
        }

        var addedCount = Math.Max(0, merged.Value.Boxes.Count - existing.Boxes.Count);
        var saved = await context.Editor.ApplyDetectedBoxesAndSaveAsync(merged.Value.Boxes).ConfigureAwait(true);
        if (saved)
        {
            var summary = context.Inference.Performance?.GetSummary();
            var suffix = string.IsNullOrWhiteSpace(summary) ? string.Empty : $" | {summary}";
            context.Snackbar.ShowSuccess(
                addedCount > 0
                    ? $"推理完成：新增 {addedCount} 个检测框（共 {merged.Value.Boxes.Count} 个）{suffix}"
                    : $"推理完成：预测框已与现有标注重合（共 {merged.Value.Boxes.Count} 个）{suffix}",
                "推理");
        }
    }
}
