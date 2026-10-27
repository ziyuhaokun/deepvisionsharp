using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Training;

/// <summary>
/// Creates a draft training job. <paramref name="ModelType"/> is required (no product-key default).
/// </summary>
public sealed record CreateTrainingJobCommand(
    string Name,
    string ModelType,
    Guid? ProjectId = null) : ICommand<Guid>;

public sealed class CreateTrainingJobCommandHandler : ICommandHandler<CreateTrainingJobCommand, Guid>
{
    private readonly ITrainingJobRepository _repository;
    private readonly IProjectRepository _projects;
    private readonly ITaskCapabilityResolver _taskCapabilities;

    public CreateTrainingJobCommandHandler(
        ITrainingJobRepository repository,
        IProjectRepository projects,
        ITaskCapabilityResolver taskCapabilities)
    {
        _repository = repository;
        _projects = projects;
        _taskCapabilities = taskCapabilities;
    }

    public async Task<Result<Guid>> Handle(CreateTrainingJobCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Guid>.Failure(TrainingValidationMessages.EmptyName);
        }

        var existing = await _repository.GetAllAsync(cancellationToken);
        if (existing.Any(job => job.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<Guid>.Failure(TrainingValidationMessages.DuplicateName(name));
        }

        if (string.IsNullOrWhiteSpace(request.ModelType))
        {
            return Result<Guid>.Failure(TrainingValidationMessages.EmptyModelType);
        }

        string modelType;
        string trainingBackendKey;

        if (request.ProjectId is Guid projectId)
        {
            var project = await _projects.GetByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                return Result<Guid>.Failure("训练任务所属项目不存在");
            }

            // Project+model composition is the single authority (compatibility + overlay + backend key).
            var execution = _taskCapabilities.ValidateExecution(project.Type);
            if (execution.IsFailure)
            {
                return Result<Guid>.Failure(execution.Error!);
            }

            var profileResult = _taskCapabilities.ForProjectAndModel(project.Type, request.ModelType);
            if (profileResult.IsFailure || profileResult.Value is null)
            {
                return Result<Guid>.Failure(profileResult.Error ?? TrainingValidationMessages.IncompatibleWithWorkflow);
            }

            modelType = profileResult.Value.ModelType;
            trainingBackendKey = profileResult.Value.TrainingBackendKey;
        }
        else
        {
            if (!ModelTypeNames.TryNormalize(request.ModelType, out modelType)
                || !ModelCapabilityCatalog.TryResolve(modelType, out var capabilities))
            {
                return Result<Guid>.Failure(TrainingValidationMessages.UnsupportedModelType(request.ModelType.Trim()));
            }

            var execution = _taskCapabilities.ValidateModelExecution(modelType);
            if (execution.IsFailure)
            {
                return Result<Guid>.Failure(execution.Error!);
            }

            trainingBackendKey = capabilities.TrainingBackendKey;
        }

        var job = TrainingJob.CreateDraft(name, request.ProjectId);
        var configureResult = job.ConfigureDraftModel(trainingBackendKey, modelType);
        if (configureResult.IsFailure || configureResult.Value is null)
        {
            return Result<Guid>.Failure(configureResult.Error ?? "无法创建训练任务");
        }

        job = configureResult.Value;
        await _repository.SaveAsync(job, cancellationToken);

        return Result<Guid>.Success(job.Id);
    }
}
