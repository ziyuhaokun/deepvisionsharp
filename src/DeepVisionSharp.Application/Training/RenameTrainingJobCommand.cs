using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Training;

public sealed record RenameTrainingJobCommand(Guid JobId, string Name) : ICommand;

public sealed class RenameTrainingJobCommandHandler : ICommandHandler<RenameTrainingJobCommand>
{
    private readonly ITrainingJobRepository _repository;

    public RenameTrainingJobCommandHandler(ITrainingJobRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> Handle(RenameTrainingJobCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(TrainingValidationMessages.EmptyName);
        }

        var job = await _repository.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null)
        {
            return Result.Failure($"训练任务 {request.JobId} 不存在");
        }

        if (job.Status != TrainingJobStatus.Draft)
        {
            return Result.Failure("任务已开始，无法修改名称");
        }

        if (string.Equals(job.Name, name, StringComparison.Ordinal))
        {
            return Result.Success();
        }

        var existing = await _repository.GetAllAsync(cancellationToken);
        if (existing.Any(other =>
                other.Id != request.JobId
                && other.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result.Failure(TrainingValidationMessages.DuplicateName(name));
        }

        var renameResult = job.RenameDraft(name);
        if (renameResult.IsFailure || renameResult.Value is null)
        {
            return Result.Failure(renameResult.Error ?? "无法重命名训练任务");
        }

        await _repository.SaveAsync(renameResult.Value, cancellationToken);

        return Result.Success();
    }
}
