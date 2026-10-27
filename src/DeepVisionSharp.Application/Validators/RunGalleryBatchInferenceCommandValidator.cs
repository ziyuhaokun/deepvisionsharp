using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Inference;

using FluentValidation;

namespace DeepVisionSharp.Application.Validators;

/// <summary>
/// Structural checks for gallery batch inference. Project/gallery ownership, label presence,
/// model compatibility and model-file existence stay in the handler.
/// </summary>
public sealed class RunGalleryBatchInferenceCommandValidator : AbstractValidator<RunGalleryBatchInferenceCommand>
{
    public RunGalleryBatchInferenceCommandValidator()
    {
        RuleFor(command => command.ConfidenceThreshold)
            .Must(threshold => threshold is > 0 and <= 1)
            .WithMessage("置信度阈值必须在 (0, 1] 范围内。");

        RuleFor(command => command.Device)
            .Must(device => InferenceDevices.TryNormalize(device, out _))
            .WithMessage("推理设备无效，仅支持 CPU 或 cuda:0。");

        RuleFor(command => command.MaxDetections)
            .InclusiveBetween(InferenceMaxDetectionsLimits.Min, InferenceMaxDetectionsLimits.Max)
            .WithMessage(
                $"最多保留目标数须在 {InferenceMaxDetectionsLimits.Min}–{InferenceMaxDetectionsLimits.Max} 之间。");
    }
}
