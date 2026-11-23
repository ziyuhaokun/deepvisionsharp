using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Presentation.Services.Labeling.WriteBack;

/// <summary>
/// General image classification write-back: top-1 class must match a project label.
/// </summary>
internal sealed class ImageClassTop1WriteBackStrategy : ILabelingInferenceWriteBackStrategy
{
    public InferenceWriteBackKind Kind => InferenceWriteBackKind.ImageClassTop1;

    public async Task<LabelingWriteBackResult> WriteBackAsync(LabelingWriteBackContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var prediction = context.Inference.Detections
            .OrderByDescending(item => item.Confidence)
            .FirstOrDefault();
        if (prediction is null)
        {
            return new(LabelingWriteBackStatus.Skipped, "分类置信度未达到阈值，未写回分类。");
        }

        var predictedName = string.IsNullOrWhiteSpace(prediction.ClassName)
            ? prediction.Label
            : prediction.ClassName;
        var projectLabel = context.Labels.LabelOptions.FirstOrDefault(item =>
            string.Equals(item.Name, predictedName, StringComparison.OrdinalIgnoreCase));
        if (projectLabel is null)
        {
            return new(LabelingWriteBackStatus.Failed, $"模型返回的类别「{predictedName}」不在当前项目类别中，未写回分类。");
        }

        var width = context.Workspace.ImageWidth > 0 ? context.Workspace.ImageWidth : context.Inference.ImageWidth;
        var height = context.Workspace.ImageHeight > 0 ? context.Workspace.ImageHeight : context.Inference.ImageHeight;
        if (width <= 0 || height <= 0)
        {
            return new(LabelingWriteBackStatus.Failed, "推理结果缺少图像尺寸，未写回分类。");
        }

        var document = new AnnotationDocument(width, height, projectLabel.Name,
            context.Editor.Boxes.Select(box => box.ToShape()).ToArray(), context.Editor.IsReviewed);
        var result = await context.Editor.ApplyInferenceAnnotationAsync(context.Target, document, cancellationToken);
        return result.Status == LabelingWriteBackStatus.Saved
            ? result with { Message = $"已保存「{context.Target.FileName}」的分类 {projectLabel.Name}（置信度 {prediction.Confidence:F2}）。" }
            : result;
    }
}
