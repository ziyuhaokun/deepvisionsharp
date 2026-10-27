using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Training;

public sealed record UpdateTrainingJobCommand(Guid JobId, TrainingJobForm Form) : ICommand<Unit>;

public sealed class UpdateTrainingJobCommandHandler : ICommandHandler<UpdateTrainingJobCommand, Unit>
{
    private readonly ITrainingJobRepository _repository;
    private readonly IProjectRepository _projects;
    private readonly ITaskCapabilityResolver _taskCapabilities;

    public UpdateTrainingJobCommandHandler(
        ITrainingJobRepository repository,
        IProjectRepository projects,
        ITaskCapabilityResolver taskCapabilities)
    {
        _repository = repository;
        _projects = projects;
        _taskCapabilities = taskCapabilities;
    }

    public async Task<Result<Unit>> Handle(UpdateTrainingJobCommand request, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null)
        {
            return Result<Unit>.Failure($"训练任务 {request.JobId} 不存在");
        }

        if (job.Status != TrainingJobStatus.Draft)
        {
            return Result<Unit>.Failure("仅草稿任务可更新配置");
        }

        var name = request.Form.ModelName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Unit>.Failure(TrainingValidationMessages.EmptyName);
        }

        var peers = job.ProjectId is Guid projectId
            ? await _repository.GetByProjectIdAsync(projectId, cancellationToken)
            : await _repository.GetAllAsync(cancellationToken);
        if (peers.Any(other =>
                other.Id != job.Id
                && other.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<Unit>.Failure(TrainingValidationMessages.DuplicateName(name));
        }

        if (string.IsNullOrWhiteSpace(request.Form.ModelType))
        {
            return Result<Unit>.Failure(TrainingValidationMessages.EmptyModelType);
        }

        string modelType;
        string trainingBackendKey;

        if (job.ProjectId is Guid owningProjectId)
        {
            var project = await _projects.GetByIdAsync(owningProjectId, cancellationToken);
            if (project is null)
            {
                return Result<Unit>.Failure("训练任务所属项目不存在");
            }

            ProjectCapabilityCatalog.EnsureActive(project.Type);
            var profileResult = _taskCapabilities.ForProjectAndModel(project.Type, request.Form.ModelType);
            if (profileResult.IsFailure || profileResult.Value is null)
            {
                return Result<Unit>.Failure(profileResult.Error ?? TrainingValidationMessages.IncompatibleWithWorkflow);
            }

            var profile = profileResult.Value;
            modelType = profile.ModelType;
            trainingBackendKey = profile.TrainingBackendKey;
        }
        else
        {
            if (!ModelTypeNames.TryNormalize(request.Form.ModelType, out modelType)
                || !ModelCapabilityCatalog.TryResolve(modelType, out var capabilities))
            {
                return Result<Unit>.Failure(TrainingValidationMessages.UnsupportedModelType(request.Form.ModelType.Trim()));
            }

            var execution = _taskCapabilities.ValidateModelExecution(modelType);
            if (execution.IsFailure) return Result<Unit>.Failure(execution.Error!);
            trainingBackendKey = capabilities.TrainingBackendKey;
        }

        var updateResult = job.UpdateDraft(name, TrainingJobFormMapper.ToConfiguration(request.Form));
        if (updateResult.IsFailure || updateResult.Value is null)
        {
            return Result<Unit>.Failure(updateResult.Error ?? "无法更新训练任务");
        }

        var configuredResult = updateResult.Value.ConfigureDraftModel(
            backend: trainingBackendKey,
            modelType: modelType);
        if (configuredResult.IsFailure || configuredResult.Value is null)
        {
            return Result<Unit>.Failure(configuredResult.Error ?? "无法更新训练任务模型类型");
        }

        await _repository.SaveAsync(configuredResult.Value, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }
}
