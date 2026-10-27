using DeepVisionSharp.Application.Models;

namespace DeepVisionSharp.Shell.Services.Labeling.WriteBack;

/// <summary>
/// General image classification write-back: top-1 class must match a project label.
/// </summary>
internal sealed class ImageClassTop1WriteBackStrategy : ILabelingInferenceWriteBackStrategy
{
    public InferenceWriteBackKind Kind => InferenceWriteBackKind.ImageClassTop1;

    public async Task WriteBackAsync(LabelingWriteBackContext context, CancellationToken cancellationToken = default)
    {
        var prediction = context.Inference.Detections
            .OrderByDescending(item => item.Confidence)
            .FirstOrDefault();
        if (prediction is null)
        {
            context.Snackbar.ShowInfo("分类置信度未达到阈值，当前样本分类保持不变。", "推理");
            return;
        }

        var predictedName = string.IsNullOrWhiteSpace(prediction.ClassName)
            ? prediction.Label
            : prediction.ClassName;
        var projectLabel = context.Labels.LabelOptions.FirstOrDefault(item =>
            string.Equals(item.Name, predictedName, StringComparison.OrdinalIgnoreCase));
        if (projectLabel is null)
        {
            // Hard fail: never invent a project class from unknown model output.
            context.Snackbar.ShowError(
                $"模型返回的类别「{predictedName}」不在当前项目类别中，未写回结果。",
                "推理");
            return;
        }

        var saved = await context.Editor.ApplyImageClassAndSaveAsync(projectLabel.Name).ConfigureAwait(true);
        if (saved)
        {
            context.Snackbar.ShowSuccess(
                $"推理完成：图像分类为 {projectLabel.Name}（置信度 {prediction.Confidence:F2}） | {context.Inference.Performance?.GetSummary()}",
                "推理");
        }
    }
}
