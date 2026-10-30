using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Application.Training;

public sealed record GetTrainingJobsByProjectQuery(Guid ProjectId) : IQuery<IReadOnlyList<TrainingJobRow>>;

public sealed class GetTrainingJobsByProjectQueryHandler : IQueryHandler<GetTrainingJobsByProjectQuery, IReadOnlyList<TrainingJobRow>>
{
    private readonly ITrainingJobRepository _repository;

    public GetTrainingJobsByProjectQueryHandler(ITrainingJobRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<TrainingJobRow>>> Handle(GetTrainingJobsByProjectQuery request, CancellationToken cancellationToken)
    {
        var jobs = await _repository.GetByProjectIdAsync(request.ProjectId, cancellationToken);
        var rows = jobs
            .OrderByDescending(job => job.StartedAt ?? DateTime.MinValue)
            .Select(MapRow)
            .ToList();

        return Result<IReadOnlyList<TrainingJobRow>>.Success(rows);
    }

    private static TrainingJobRow MapRow(TrainingJob job)
    {
        return new TrainingJobRow
        {
            Id = job.Id,
            Name = job.Name,
            ModelType = job.ModelType,
            Status = job.Status,
            StartedAt = job.StartedAt,
            FinishedAt = job.FinishedAt,
            LogFile = job.LogFile,
            BestModelPath = job.BestModelPath,
            LastModelPath = job.LastModelPath,
            OutputRoot = job.OutputRoot,
            FailureMessage = job.FailureMessage,
            ConfigDatasetSplitMode = job.Configuration?.DatasetSplitMode,
            ConfigGalleryRoles = job.Configuration?.GalleryRoles,
            ConfigEpochs = job.Configuration?.Epochs,
            ConfigEarlyStoppingPatience = job.Configuration?.EarlyStoppingPatience,
            ConfigBatchSize = job.Configuration?.BatchSize,
            ConfigLearningRate = job.Configuration?.LearningRate,
            ConfigOptimizer = job.Configuration?.Optimizer,
            ConfigGalleryId = job.Configuration?.GalleryId,
            ConfigGalleryIds = job.Configuration?.GalleryIds,
            ConfigValidationRatio = job.Configuration?.ValidationRatio,
            ConfigNetworkInputSize = job.Configuration?.NetworkInputSize,
            ConfigNetworkInputWidth = job.Configuration?.NetworkInputWidth,
            ConfigNetworkInputHeight = job.Configuration?.NetworkInputHeight,
            ConfigClassificationAugmentation = job.Configuration?.ClassificationAugmentation,
            ConfigDetectionOptions = job.Configuration?.DetectionOptions
        };
    }
}
