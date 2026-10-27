using FluentValidation;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Validators;

public sealed class CreateTrainingJobCommandValidator : AbstractValidator<CreateTrainingJobCommand>
{
    public CreateTrainingJobCommandValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty()
            .WithMessage(TrainingValidationMessages.EmptyName)
            .MaximumLength(120);

        // 与 CreateTrainingJobCommandHandler 手写校验一致：ModelType 必须为非空的产品类型。
        // 兼容性（项目工作流 × 模型族）由 Handler 的 ForProjectAndModel 作为唯一权威校验。
        RuleFor(command => command.ModelType)
            .NotEmpty()
            .WithMessage(TrainingValidationMessages.EmptyModelType)
            .Must(ModelCapabilityCatalog.IsSupported)
            .WithMessage(command => TrainingValidationMessages.UnsupportedModelType(command.ModelType));
    }
}

/// <summary>
/// Shared field rules for training job forms.
/// Dataset completeness is intentionally NOT required here so draft auto-save
/// (e.g. selecting a library model first) can persist partial configuration.
/// </summary>
public sealed class TrainingJobFormFieldValidator : AbstractValidator<TrainingJobForm>
{
    public TrainingJobFormFieldValidator()
    {
        RuleFor(form => form.ModelName)
            .NotEmpty()
            .WithMessage("请输入实验名称");

        RuleFor(form => form.ModelType)
            .NotEmpty()
            .WithMessage(TrainingValidationMessages.EmptyModelType)
            .Must(ModelCapabilityCatalog.IsSupported)
            .WithMessage(form => TrainingValidationMessages.UnsupportedModelType(form.ModelType));

        RuleFor(form => form.BatchSize)
            .InclusiveBetween(1, 32);

        RuleFor(form => form.Epochs)
            .InclusiveBetween(1, 100000);

        RuleFor(form => form.Lr)
            .InclusiveBetween(0.00005, 0.05);

        RuleFor(form => form)
            .Must(form => form.DatasetSplitMode != TrainingDatasetSplitMode.AutoSplit
                || form.ValidationRatio is >= 0 and <= 0.5)
            .WithMessage(TrainingValidationMessages.ValidationRatioRange);

        When(form => NetworkInputSizePolicy.ForModelType(form.ModelType) is not null, () =>
        {
            RuleFor(form => form.NetworkInputWidth)
                .Must((form, size) =>
                {
                    var policy = NetworkInputSizePolicy.ForModelType(form.ModelType);
                    return policy is not null && policy.IsValid(size);
                })
                .WithMessage(form =>
                {
                    var policy = NetworkInputSizePolicy.ForModelType(form.ModelType);
                    return policy is null
                        ? "网络输入宽度无效。"
                        : $"网络输入宽度须在 {policy.Min}–{policy.Max} 之间且为 {policy.Step} 的倍数。";
                });

            RuleFor(form => form.NetworkInputHeight)
                .Must((form, size) =>
                {
                    var policy = NetworkInputSizePolicy.ForModelType(form.ModelType);
                    return policy is not null && policy.IsValid(size);
                })
                .WithMessage(form =>
                {
                    var policy = NetworkInputSizePolicy.ForModelType(form.ModelType);
                    return policy is null
                        ? "网络输入高度无效。"
                        : $"网络输入高度须在 {policy.Min}–{policy.Max} 之间且为 {policy.Step} 的倍数。";
                });
        });

        When(form => ModelTypeNames.IsClassificationModelType(form.ModelType), () =>
        {
            RuleFor(form => form.ClassificationAugmentation)
                .Must(augmentation =>
                {
                    var value = ClassificationAugmentation.Normalize(
                        augmentation ?? ClassificationAugmentation.Disabled);
                    return value.FlipLeftRight is >= 0 and <= 1
                        && value.FlipUpDown is >= 0 and <= 1;
                })
                .WithMessage("分类数据增强参数超出允许范围");
        });
    }
}

/// <summary>
/// Full form validation for starting training (requires dataset selection).
/// </summary>
public sealed class TrainingJobFormValidator : AbstractValidator<TrainingJobForm>
{
    public TrainingJobFormValidator()
    {
        Include(new TrainingJobFormFieldValidator());

        RuleFor(form => form)
            .Must(form => TrainingJobFormMapper.ResolveParticipatingGalleryIds(form).Count > 0)
            .WithMessage(TrainingValidationMessages.SelectDataset);
    }
}

public sealed class StartTrainingJobCommandValidator : AbstractValidator<StartTrainingJobCommand>
{
    public StartTrainingJobCommandValidator(
        IProjectRepository projects,
        ITaskCapabilityResolver taskCapabilities)
    {
        RuleFor(command => command.Resume)
            .Must(resume => !resume)
            .WithMessage(TrainingValidationMessages.ResumeNotSupported);

        RuleFor(command => command.Form)
            .SetValidator(new TrainingJobFormValidator());

        RuleFor(command => command)
            .MustAsync(async (command, cancellationToken) =>
            {
                var project = await projects.GetByIdAsync(command.ProjectId, cancellationToken);
                if (project is null)
                {
                    return false;
                }

                return taskCapabilities.ForProjectAndModel(project.Type, command.Form.ModelType).IsSuccess;
            })
            .WithMessage(TrainingValidationMessages.IncompatibleWithWorkflow);
    }
}

public sealed class UpdateTrainingJobCommandValidator : AbstractValidator<UpdateTrainingJobCommand>
{
    // Draft auto-save must accept partial forms (model first, dataset later).
    // 项目工作流 × 模型族兼容性校验由 UpdateTrainingJobCommandHandler 作为唯一权威执行
    // （Handler 需要 ForProjectAndModel 返回的归一化 ModelType / BackendKey，避免 validator
    // 与 handler 各自查库重复校验同一规则）。
    public UpdateTrainingJobCommandValidator()
    {
        RuleFor(command => command.Form)
            .SetValidator(new TrainingJobFormFieldValidator());
    }
}
