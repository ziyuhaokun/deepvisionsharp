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

    public async Task<LabelingWriteBackResult> WriteBackAsync(LabelingWriteBackContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Inference.Boxes.Count == 0)
        {
            return new(LabelingWriteBackStatus.Skipped, "未检测到高于阈值的目标框，未写回标注。");
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
            return new(LabelingWriteBackStatus.Failed, merged.Error ?? "无法合并预测框，未写回标注。");
        }

        if (merged.Value is null)
        {
            return new(LabelingWriteBackStatus.Skipped, "预测框未命中当前项目类别或尺寸无效，未写回标注。");
        }

        var addedCount = merged.Value.Boxes.Count - existing.Boxes.Count;
        if (addedCount == 0 && existing.IsReviewed)
        {
            return new(LabelingWriteBackStatus.Skipped, "预测框与现有标注重合，未新增标注。");
        }

        var result = await context.Editor.ApplyInferenceAnnotationAsync(context.Target, merged.Value, cancellationToken);
        if (result.Status != LabelingWriteBackStatus.Saved)
        {
            return result;
        }

        var summary = context.Inference.Performance?.GetSummary();
        var suffix = string.IsNullOrWhiteSpace(summary) ? string.Empty : $" | {summary}";
        return result with { Message = $"已保存「{context.Target.FileName}」，新增 {addedCount} 个检测框。{suffix}" };
    }
}
