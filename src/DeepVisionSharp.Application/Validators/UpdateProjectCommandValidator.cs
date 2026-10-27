using FluentValidation;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Validators;

public sealed class UpdateProjectCommandValidator : AbstractValidator<UpdateProjectCommand>
{
    public UpdateProjectCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("项目 ID 不能为空");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("项目名称不能为空")
            .MaximumLength(100).WithMessage("项目名称不能超过100个字符")
            .Matches(@"^[\u4e00-\u9fa5a-zA-Z0-9_\-\s]+$")
            .WithMessage("项目名称只能包含中文、字母、数字、下划线、连字符和空格");

        RuleFor(x => x.Description)
            .MaximumLength(500).WithMessage("项目描述不能超过500个字符");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("项目类型不能为空")
            .Must(ProjectTypes.IsValid)
            .WithMessage("项目类型无效");
    }
}
