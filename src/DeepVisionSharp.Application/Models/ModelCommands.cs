using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Models;

public sealed record GetModelsByProjectQuery(Guid ProjectId) : IQuery<IReadOnlyList<TrainedModel>>;

public sealed class GetModelsByProjectQueryHandler : IQueryHandler<GetModelsByProjectQuery, IReadOnlyList<TrainedModel>>
{
    private readonly IModelRepository _models;

    public GetModelsByProjectQueryHandler(IModelRepository models)
    {
        _models = models;
    }

    public async Task<Result<IReadOnlyList<TrainedModel>>> Handle(GetModelsByProjectQuery request, CancellationToken cancellationToken)
    {
        var models = await _models.ListByProjectAsync(request.ProjectId, cancellationToken);
        return Result<IReadOnlyList<TrainedModel>>.Success(models);
    }
}

public sealed record DeleteModelCommand(Guid ProjectId, Guid ModelId) : ICommand;

public sealed class DeleteModelCommandHandler : ICommandHandler<DeleteModelCommand>
{
    private readonly IModelRepository _models;

    public DeleteModelCommandHandler(IModelRepository models)
    {
        _models = models;
    }

    public async Task<Result> Handle(DeleteModelCommand request, CancellationToken cancellationToken)
    {
        var deleted = await _models.DeleteAsync(request.ProjectId, request.ModelId, cancellationToken);
        return deleted
            ? Result.Success()
            : Result.Failure("未找到要删除的模型");
    }
}
