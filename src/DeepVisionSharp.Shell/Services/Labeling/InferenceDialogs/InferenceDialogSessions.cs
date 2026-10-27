using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Shell.Services.Dialogs;
using DeepVisionSharp.Shell.ViewModels.Labeling;

namespace DeepVisionSharp.Shell.Services.Labeling.InferenceDialogs;

internal abstract class InferenceDialogSessionBase : IInferenceDialogSession
{
    public abstract AppDialogKind DialogKind { get; }

    public abstract string Description { get; }

    public abstract string NoModelsHint { get; }

    public abstract string ConfidenceHint { get; }

    public abstract string FooterHint { get; }

    public abstract string PolicySectionTitle { get; }

    public virtual bool ShowsMaxDetections(InferenceModelOption? selected) => true;

    public virtual bool ShowsBatchSize(InferenceModelOption? selected) => true;

    public virtual string? FixedBatchHint(InferenceModelOption? selected) => null;

    public abstract InferenceConfig BuildConfig(InferenceDialogBuildContext context);

    public virtual string FormatConfigSummary(InferenceDialogBuildContext context)
    {
        if (context.SelectedModel is null)
        {
            return "未配置模型";
        }

        return $"{context.SelectedModel.Name} · {context.DeviceDisplayName} · conf {context.Confidence:F2}";
    }

    public virtual string FormatButtonToolTip(InferenceDialogBuildContext context) =>
        $"左键推理（{FormatConfigSummary(context)}）";

    protected static InferenceConfig CreateConfig(
        InferenceDialogBuildContext context,
        int maxDetections,
        int batchSize) =>
        new()
        {
            ModelId = context.SelectedModel?.Id,
            ConfidenceThreshold = context.Confidence,
            MaxDetections = maxDetections,
            BatchSize = batchSize,
            Device = context.Device
        };
}

/// <summary>General image classification · whole image top-1.</summary>
internal sealed class ClassificationInferenceDialogSession : InferenceDialogSessionBase
{
    public static ClassificationInferenceDialogSession Instance { get; } = new();

    public override AppDialogKind DialogKind => AppDialogKind.AutoInferenceClassification;

    public override string Description =>
        "使用训练好的分类模型判断当前整张图像所属的项目类别，并写回当前样本。";

    public override string NoModelsHint => "未找到可用的图像分类模型，请先训练模型";

    public override string ConfidenceHint => "低于此值的 top-1 分类结果将被忽略（推荐 0.5–0.8）";

    public override string FooterHint =>
        "分类模型为整图分类：无需切片；置信度为 top-1 概率阈值。结果写入项目类别。";

    public override string PolicySectionTitle => "网络输入（只读）";

    public override bool ShowsMaxDetections(InferenceModelOption? selected) => false;

    public override bool ShowsBatchSize(InferenceModelOption? selected) => false;

    public override string? FixedBatchHint(InferenceModelOption? selected) => "分类模型固定单图推理";

    public override InferenceConfig BuildConfig(InferenceDialogBuildContext context) =>
        CreateConfig(context, maxDetections: 1, batchSize: 1);

    public override string FormatConfigSummary(InferenceDialogBuildContext context) =>
        context.SelectedModel is null
            ? "未配置模型"
            : $"{context.SelectedModel.Name} · 分类 · {context.DeviceDisplayName} · conf {context.Confidence:F2}";

    public override string FormatButtonToolTip(InferenceDialogBuildContext context) =>
        $"整图分类（{FormatConfigSummary(context)}）";
}
