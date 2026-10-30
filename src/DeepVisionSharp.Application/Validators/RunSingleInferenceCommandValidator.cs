using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;

using FluentValidation;

namespace DeepVisionSharp.Application.Validators;

/// <summary>
/// Structural checks for single-image inference. File existence and backend availability
/// stay in the handler because they depend on the environment.
/// </summary>
public sealed class RunSingleInferenceCommandValidator : AbstractValidator<RunSingleInferenceCommand>
{
    public RunSingleInferenceCommandValidator()
    {
        RuleFor(command => command.ModelType)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("模型类型不能为空")
            .Must(ModelTypeNames.IsKnownModelType)
            .WithMessage(command => $"不支持的推理模型类型：{command.ModelType.Trim()}");

        RuleFor(command => command.ModelPath)
            .NotEmpty()
            .WithMessage("模型路径不能为空");

        RuleFor(command => command.ImagePath)
            .NotEmpty()
            .WithMessage("图像路径不能为空");

        RuleFor(command => command.Config)
            .NotNull()
            .WithMessage("推理配置不能为空");
    }
}
