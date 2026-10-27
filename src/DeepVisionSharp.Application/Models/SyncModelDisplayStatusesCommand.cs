using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Models;

public sealed record SyncModelDisplayStatusesCommand(Guid ProjectId) : ICommand;

public sealed class SyncModelDisplayStatusesCommandHandler : ICommandHandler<SyncModelDisplayStatusesCommand>
{
    private readonly IModelRepository _models;
    private readonly ITrainingJobRepository _trainingJobs;

    public SyncModelDisplayStatusesCommandHandler(
        IModelRepository models,
        ITrainingJobRepository trainingJobs)
    {
        _models = models;
        _trainingJobs = trainingJobs;
    }

    public async Task<Result> Handle(SyncModelDisplayStatusesCommand request, CancellationToken cancellationToken)
    {
        var models = await _models.ListByProjectAsync(request.ProjectId, cancellationToken);
        if (models.Count == 0)
        {
            return Result.Success();
        }

        var jobs = await _trainingJobs.GetByProjectIdAsync(request.ProjectId, cancellationToken);
        var hasRunningTraining = jobs.Any(job => TrainingJobTransitions.IsActive(job.Status));

        foreach (var model in models)
        {
            var normalizedStatus = TrainedModelStatuses.Normalize(model.Status, hasRunningTraining);
            if (normalizedStatus == model.Status)
            {
                continue;
            }

            var updated = model.With(status: normalizedStatus);
            await _models.SaveAsync(request.ProjectId, updated, cancellationToken);
        }

        return Result.Success();
    }
}
