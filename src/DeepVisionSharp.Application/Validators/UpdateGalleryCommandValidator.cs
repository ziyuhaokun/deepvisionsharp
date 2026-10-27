using FluentValidation;
using DeepVisionSharp.Application.Galleries;

namespace DeepVisionSharp.Application.Validators;

public sealed class UpdateGalleryCommandValidator : AbstractValidator<UpdateGalleryCommand>
{
    public UpdateGalleryCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("图库 ID 不能为空");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("图库名称不能为空")
            .MaximumLength(100).WithMessage("图库名称不能超过100个字符");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("图库描述不能超过500个字符");
    }
}
