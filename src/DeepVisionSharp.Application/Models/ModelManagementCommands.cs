using MediatR;
using DeepVisionSharp.Application.Common;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Projects;
using DeepVisionSharp.Application.Training;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Models;

public sealed record GetModelByIdQuery(Guid ModelId) : IQuery<TrainedModel>;

public sealed class GetModelByIdQueryHandler : IQueryHandler<GetModelByIdQuery, TrainedModel>
{
    private readonly IModelRepository _models;

    public GetModelByIdQueryHandler(IModelRepository models)
    {
        _models = models;
    }

    public async Task<Result<TrainedModel>> Handle(GetModelByIdQuery request, CancellationToken cancellationToken)
    {
        var model = await _models.GetByIdAsync(request.ModelId, cancellationToken);
        return model is null
            ? Result<TrainedModel>.Failure($"模型 {request.ModelId} 不存在")
            : Result<TrainedModel>.Success(model);
    }
}

public sealed record CreateModelCommand(
    Guid ProjectId,
    string Name,
    string ModelType,
    string CheckpointPath,
    IReadOnlyList<string> Classes,
    string? Note = null,
    TrainedModelStatus Status = TrainedModelStatus.Creating,
    int MaxDetections = 500) : ICommand<Guid>;

public sealed class CreateModelCommandHandler : ICommandHandler<CreateModelCommand, Guid>
{
    private readonly IModelRepository _models;
    private readonly IModelCheckpointFileService _checkpointFiles;
    private readonly IProjectRepository _projects;
    private readonly ITaskCapabilityResolver _taskCapabilities;

    public CreateModelCommandHandler(
        IModelRepository models,
        IModelCheckpointFileService checkpointFiles,
        IProjectRepository projects,
        ITaskCapabilityResolver taskCapabilities)
    {
        _models = models;
        _checkpointFiles = checkpointFiles;
        _projects = projects;
        _taskCapabilities = taskCapabilities;
    }

    public async Task<Result<Guid>> Handle(CreateModelCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Guid>.Failure("请输入模型名称");
        }

        if (await _models.ExistsWithNameAsync(request.ProjectId, name, cancellationToken: cancellationToken))
        {
            return Result<Guid>.Failure($"模型名称 \"{name}\" 在该项目中已存在");
        }

        if (!_checkpointFiles.Exists(request.CheckpointPath))
        {
            return Result<Guid>.Failure("模型 checkpoint 文件不存在");
        }

        if (string.IsNullOrWhiteSpace(request.ModelType))
        {
            return Result<Guid>.Failure("模型类型不能为空");
        }

        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        string modelType;
        if (project is not null)
        {
            ProjectCapabilityCatalog.EnsureActive(project.Type);
            var profileResult = _taskCapabilities.ForProjectAndModel(project.Type, request.ModelType);
            if (profileResult.IsFailure || profileResult.Value is null)
            {
                return Result<Guid>.Failure(profileResult.Error ?? TrainingValidationMessages.IncompatibleWithWorkflow);
            }

            modelType = profileResult.Value.ModelType;
        }
        else if (!ModelTypeNames.TryNormalize(request.ModelType, out modelType)
                 || !ModelCapabilityCatalog.IsSupported(modelType))
        {
            return Result<Guid>.Failure($"不支持的模型类型：{request.ModelType.Trim()}");
        }

        var model = TrainedModel.CreateNew(
            request.ProjectId,
            name,
            modelType,
            request.Status,
            request.Note ?? string.Empty,
            request.Classes,
            request.CheckpointPath,
            request.MaxDetections);

        await _models.SaveAsync(request.ProjectId, model, cancellationToken);
        return Result<Guid>.Success(model.Id);
    }
}

public sealed record UpdateModelCommand(
    Guid ProjectId,
    Guid ModelId,
    string? Name = null,
    string? ModelType = null,
    IReadOnlyList<string>? Classes = null,
    string? CheckpointPath = null,
    TrainedModelStatus? Status = null,
    string? Note = null,
    int? MaxDetections = null) : ICommand;

public sealed class UpdateModelCommandHandler : ICommandHandler<UpdateModelCommand>
{
    private readonly IModelRepository _models;
    private readonly ITrainedModelArtifactService _artifactService;
    private readonly IModelCheckpointFileService _checkpointFiles;
    private readonly IProjectRepository _projects;
    private readonly ITaskCapabilityResolver _taskCapabilities;

    public UpdateModelCommandHandler(
        IModelRepository models,
        ITrainedModelArtifactService artifactService,
        IModelCheckpointFileService checkpointFiles,
        IProjectRepository projects,
        ITaskCapabilityResolver taskCapabilities)
    {
        _models = models;
        _artifactService = artifactService;
        _checkpointFiles = checkpointFiles;
        _projects = projects;
        _taskCapabilities = taskCapabilities;
    }

    public async Task<Result> Handle(UpdateModelCommand request, CancellationToken cancellationToken)
    {
        var model = await _models.GetByIdAsync(request.ModelId, cancellationToken);
        if (model is null || model.ProjectId != request.ProjectId)
        {
            return Result.Failure($"模型 {request.ModelId} 不存在");
        }

        // 模型类型在创建后不可修改；调用方应省略 ModelType，或传入与现有一致的值。
        if (!string.IsNullOrWhiteSpace(request.ModelType)
            && !string.Equals(request.ModelType.Trim(), model.ModelType, StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure("模型类型在创建后不可修改");
        }

        var effectiveModelType = model.ModelType;
        var effectiveClasses = request.Classes ?? model.Classes;
        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        if (project is not null)
        {
            ProjectCapabilityCatalog.EnsureActive(project.Type);
            var profileResult = _taskCapabilities.ForProjectAndModel(project.Type, effectiveModelType);
            if (profileResult.IsFailure || profileResult.Value is null)
            {
                return Result.Failure(profileResult.Error ?? TrainingValidationMessages.IncompatibleWithWorkflow);
            }

            effectiveModelType = profileResult.Value.ModelType;
        }
        else if (!ModelCapabilityCatalog.IsSupported(effectiveModelType))
        {
            return Result.Failure($"不支持的模型类型：{effectiveModelType}");
        }

        var trimmedName = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedName)
            && !string.Equals(trimmedName, model.Name, StringComparison.OrdinalIgnoreCase)
            && await _models.ExistsWithNameAsync(request.ProjectId, trimmedName, request.ModelId, cancellationToken))
        {
            return Result.Failure($"模型名称 \"{trimmedName}\" 在该项目中已存在");
        }

        var checkpointPath = request.CheckpointPath ?? model.CheckpointPath;
        if (!string.IsNullOrWhiteSpace(trimmedName)
            && !string.Equals(trimmedName, model.Name, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(model.CheckpointPath)
            && _checkpointFiles.Exists(model.CheckpointPath))
        {
            var renameResult = _artifactService.TryRenameCheckpointFiles(model.CheckpointPath, trimmedName);
            if (renameResult.IsFailure || string.IsNullOrWhiteSpace(renameResult.Value))
            {
                return Result.Failure(renameResult.Error ?? "重命名模型文件失败");
            }

            checkpointPath = renameResult.Value;
        }

        var updated = model.With(
            name: trimmedName,
            // 类型不可变：始终保留已有 ModelType
            modelType: null,
            status: request.Status,
            note: request.Note is null ? null : request.Note.Trim(),
            classes: request.Classes,
            checkpointPath: string.IsNullOrWhiteSpace(checkpointPath) ? null : checkpointPath,
            maxDetections: request.MaxDetections);

        await _models.SaveAsync(request.ProjectId, updated, cancellationToken);
        return Result.Success();
    }
}

public sealed record SaveTrainedModelCommand(
    Guid ProjectId,
    Guid JobId,
    string? ModelName = null,
    Guid? TargetModelId = null,
    TrainingCheckpointSelection CheckpointSelection = TrainingCheckpointSelection.Best) : ICommand<Guid>;

public sealed class SaveTrainedModelCommandHandler : ICommandHandler<SaveTrainedModelCommand, Guid>
{
    private readonly ITrainingJobRepository _trainingJobs;
    private readonly ITrainedModelArtifactService _artifactService;
    private readonly IProjectRepository _projects;
    private readonly IMediator _mediator;

    public SaveTrainedModelCommandHandler(
        ITrainingJobRepository trainingJobs,
        ITrainedModelArtifactService artifactService,
        IProjectRepository projects,
        IMediator mediator)
    {
        _trainingJobs = trainingJobs;
        _artifactService = artifactService;
        _projects = projects;
        _mediator = mediator;
    }

    public async Task<Result<Guid>> Handle(SaveTrainedModelCommand request, CancellationToken cancellationToken)
    {
        var job = await _trainingJobs.GetByIdAsync(request.JobId, cancellationToken);
        if (job is null || job.ProjectId != request.ProjectId)
        {
            return Result<Guid>.Failure($"训练任务 {request.JobId} 不存在");
        }

        if (job.Status is not (TrainingJobStatus.Succeeded or TrainingJobStatus.Cancelled or TrainingJobStatus.Failed))
        {
            return Result<Guid>.Failure("仅训练成功、已取消或训练失败但有可用权重的任务可保存模型");
        }

        var modelName = string.IsNullOrWhiteSpace(request.ModelName) ? job.Name : request.ModelName.Trim();
        if (string.IsNullOrWhiteSpace(job.ModelType))
        {
            return Result<Guid>.Failure("训练任务缺少模型类型，无法保存到模型库");
        }

        if (!ModelTypeNames.TryNormalize(job.ModelType, out var modelType))
        {
            return Result<Guid>.Failure(TrainingValidationMessages.UnsupportedModelType(job.ModelType.Trim()));
        }

        var sourceCheckpointResult = _artifactService.ResolveCheckpointPath(
            new ModelArtifactCheckpointRequest(
                modelType,
                job.BestModelPath,
                job.LastModelPath,
                job.OutputRoot,
                request.CheckpointSelection));
        if (sourceCheckpointResult.IsFailure || string.IsNullOrWhiteSpace(sourceCheckpointResult.Value))
        {
            return Result<Guid>.Failure(sourceCheckpointResult.Error ?? "找不到当前模型类型的训练产物");
        }

        var sourceCheckpointPath = sourceCheckpointResult.Value;
        var classesResult = await _artifactService.RecoverClassesAsync(
            new ModelArtifactClassRecoveryRequest(
                modelType,
                job.DataRoot,
                job.OutputRoot,
                sourceCheckpointPath,
                CandidateClasses: null),
            cancellationToken);

        var project = await _projects.GetByIdAsync(request.ProjectId, cancellationToken);
        IReadOnlyList<string> classes;
        if (project is not null)
        {
            ProjectCapabilityCatalog.EnsureActive(project.Type);
        }
        if (classesResult.IsSuccess && classesResult.Value is { Count: > 0 })
        {
            classes = classesResult.Value;
        }
        else
        {
            // Last resort for interrupted runs: project label list / fixed OK·NG for variation.
            classes = ResolveFallbackClasses(project);
            if (classes.Count == 0)
            {
                return Result<Guid>.Failure(
                    classesResult.Error
                    ?? "无法解析模型类别信息。训练中断时需保留输出目录中的数据集，或确保项目已配置类别。");
            }
        }

        var formResult = MapTrainingForm(job, modelName);
        if (formResult.IsFailure || formResult.Value is null)
        {
            return Result<Guid>.Failure(formResult.Error ?? "训练任务缺少有效配置");
        }

        var form = formResult.Value;
        var artifactResult = await _artifactService.SaveModelArtifactAsync(
            new ModelArtifactSaveRequest(
                request.ProjectId,
                modelName,
                modelType,
                sourceCheckpointPath,
                classes,
                form,
                job.DataRoot,
                job.OutputRoot,
                job.BestModelPath,
                job.LastModelPath,
                request.CheckpointSelection,
                request.JobId),
            cancellationToken);
        if (artifactResult.IsFailure || artifactResult.Value is null)
        {
            return Result<Guid>.Failure(artifactResult.Error ?? "保存模型产物失败");
        }

        var artifact = artifactResult.Value;

        if (request.TargetModelId is Guid targetModelId)
        {
            var modelResult = await _mediator.Send(new GetModelByIdQuery(targetModelId), cancellationToken);
            if (modelResult.IsFailure || modelResult.Value is null)
            {
                return Result<Guid>.Failure(modelResult.Error ?? "目标模型不存在");
            }

            if (modelResult.Value.ProjectId != request.ProjectId)
            {
                return Result<Guid>.Failure("目标模型不属于当前项目");
            }

            // 覆盖已有库模型时保留原模型类型（创建后不可改）。
            var updateResult = await _mediator.Send(new UpdateModelCommand(
                request.ProjectId,
                targetModelId,
                Name: modelName,
                Classes: classes,
                CheckpointPath: artifact.CheckpointPath,
                Status: TrainedModelStatus.Ready,
                Note: "训练更新"),
                cancellationToken);

            if (updateResult.IsFailure)
            {
                return Result<Guid>.Failure(updateResult.Error ?? "更新模型记录失败");
            }

            return Result<Guid>.Success(targetModelId);
        }

        var createResult = await _mediator.Send(new CreateModelCommand(
            request.ProjectId,
            modelName,
            modelType,
            artifact.CheckpointPath,
            classes,
            Note: "训练保存",
            Status: TrainedModelStatus.Ready), cancellationToken);

        if (createResult.IsFailure || createResult.Value == Guid.Empty)
        {
            return Result<Guid>.Failure(createResult.Error ?? "保存模型记录失败");
        }

        return Result<Guid>.Success(createResult.Value);
    }

    /// <summary>
    /// When sidecar and on-disk dataset class maps are missing (interrupted training),
    /// fall back to project labels.
    /// </summary>
    private static IReadOnlyList<string> ResolveFallbackClasses(Project? project)
    {
        if (project?.Labels is { Count: > 0 })
        {
            return project.Labels
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return Array.Empty<string>();
    }

    private static Result<TrainingJobForm> MapTrainingForm(TrainingJob job, string modelName)
    {
        var config = job.Configuration;
        if (string.IsNullOrWhiteSpace(job.ModelType))
        {
            return Result<TrainingJobForm>.Failure("训练任务缺少模型类型");
        }

        if (!ModelTypeNames.TryNormalize(job.ModelType, out var modelType)
            || !ModelCapabilityCatalog.TryResolve(modelType, out var capabilities))
        {
            return Result<TrainingJobForm>.Failure(TrainingValidationMessages.UnsupportedModelType(job.ModelType.Trim()));
        }

        var galleryIds = config?.GalleryIds?.ToList() ?? new List<Guid>();
        var roles = config?.GalleryRoles?.ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<Guid, GalleryDatasetRole>();
        var defaultInput = NetworkInputSizePolicy.ForModelType(modelType)?.Default ?? 512;

        return Result<TrainingJobForm>.Success(new TrainingJobForm
        {
            ModelName = modelName,
            ModelType = modelType,
            DatasetSplitMode = config?.DatasetSplitMode ?? TrainingDatasetSplitMode.AutoSplit,
            SelectedGalleryIds = galleryIds,
            GalleryRoles = roles,
            Epochs = config?.Epochs ?? 50,
            BatchSize = config?.BatchSize ?? 16,
            Lr = config?.LearningRate ?? 0.005,
            Optimizer = config?.Optimizer ?? "sgd",
            ValidationRatio = config?.ValidationRatio ?? 0.1,
            NetworkInputWidth = config?.NetworkInputWidth ?? config?.NetworkInputSize ?? defaultInput,
            NetworkInputHeight = config?.NetworkInputHeight ?? config?.NetworkInputSize ?? defaultInput,
            ClassificationAugmentation = config?.ClassificationAugmentation
        });
    }
}
