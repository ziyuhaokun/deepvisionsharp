using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Projects;

public sealed record CreateProjectCommand(string Name, string Description, string Type) : ICommand<Project>;

public sealed class CreateProjectCommandHandler : ICommandHandler<CreateProjectCommand, Project>
{
    private readonly IProjectRepository _projects;
    private readonly IProjectLabelService _labels;

    public CreateProjectCommandHandler(
        IProjectRepository projects,
        IProjectLabelService labels)
    {
        _projects = projects;
        _labels = labels;
    }

    public async Task<Result<Project>> Handle(CreateProjectCommand request, CancellationToken cancellationToken)
    {
        if (!ProjectTypes.IsValid(request.Type))
        {
            return Result<Project>.Failure(
                string.IsNullOrWhiteSpace(request.Type)
                    ? "项目类型不能为空。"
                    : $"不支持的项目类型：{request.Type.Trim()}。当前支持图像分类与目标检测。");
        }

        var type = ProjectTypes.Normalize(request.Type);
        var name = request.Name.Trim();
        if (await _projects.ExistsByNameAsync(name, cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            return Result<Project>.Failure($"项目名称「{name}」已存在。");
        }

        var project = await _projects.CreateAsync(
            name,
            request.Description.Trim(),
            type,
            cancellationToken).ConfigureAwait(false);

        return Result<Project>.Success(project);
    }
}
