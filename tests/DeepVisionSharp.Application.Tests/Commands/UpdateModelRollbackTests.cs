using Xunit;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.Entities;
using DeepVisionSharp.Domain.Repositories;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Tests.Commands;

public sealed class UpdateModelRollbackTests
{
    [Fact]
    public async Task UpdateModel_triggers_rollback_when_database_save_fails()
    {
        var projectId = Guid.NewGuid();
        var modelId = Guid.NewGuid();
        const string oldName = "model_initial";
        const string newName = "model_renamed";
        const string originalPath = @"C:\models\model_initial.torchsharp";
        const string renamedPath = @"C:\models\model_renamed.torchsharp";

        var existingModel = TrainedModel.CreateNew(
            projectId,
            oldName,
            ModelTypeNames.ResNet18,
            TrainedModelStatus.Ready,
            note: "",
            classes: ["OK", "NG"],
            originalPath,
            maxDetections: 1);

        var fakeModels = new FakeModelRepository(existingModel)
        {
            ThrowOnSave = true // Simulate SQLite database failure on save
        };

        var fakeArtifacts = new FakeArtifactService(renamedPath, originalPath);
        var fakeCheckpoints = new FakeCheckpointFileService();
        var fakeProjects = new FakeProjectRepository(new Project(projectId, "TestProj", DateTimeOffset.UtcNow, labels: ["OK", "NG"]));
        var taskCapabilities = new FakeTaskCapabilityResolver();

        var handler = new UpdateModelCommandHandler(
            fakeModels,
            fakeArtifacts,
            fakeCheckpoints,
            fakeProjects,
            taskCapabilities);

        var command = new UpdateModelCommand(projectId, modelId, Name: newName);

        var result = await handler.Handle(command, CancellationToken.None);

        // Command must report failure from the database
        Assert.True(result.IsFailure);
        Assert.Contains("保存模型元数据失败", result.Error);

        // Forward rename was called
        Assert.Equal(1, fakeArtifacts.ForwardRenameCalls);

        // Compensating rollback rename was called to restore original file name!
        Assert.Equal(1, fakeArtifacts.RollbackRenameCalls);
    }

    [Fact]
    public void InferenceConfig_IsValid_accepts_UseModelNmsIou_sentinel_and_explicit_range()
    {
        var modelDefaultConfig = new InferenceConfig
        {
            ModelId = Guid.NewGuid(),
            ConfidenceThreshold = 0.25,
            NmsIouThreshold = InferenceConfig.UseModelNmsIou,
            MaxDetections = 100,
            BatchSize = 8,
            Device = InferenceDevices.Cpu
        };
        Assert.True(modelDefaultConfig.IsValid);

        var explicitConfig = new InferenceConfig
        {
            ModelId = Guid.NewGuid(),
            ConfidenceThreshold = 0.25,
            NmsIouThreshold = 0.65,
            MaxDetections = 100,
            BatchSize = 8,
            Device = InferenceDevices.Cpu
        };
        Assert.True(explicitConfig.IsValid);

        var invalidLowConfig = new InferenceConfig
        {
            ModelId = Guid.NewGuid(),
            ConfidenceThreshold = 0.25,
            NmsIouThreshold = 0.01,
            MaxDetections = 100,
            BatchSize = 8,
            Device = InferenceDevices.Cpu
        };
        Assert.False(invalidLowConfig.IsValid);
    }

    [Fact]
    public async Task SaveTrainedModel_passes_detection_MaxDetections_on_both_create_and_overwrite_branches()
    {
        var projectId = Guid.NewGuid();
        var targetModelId = Guid.NewGuid();
        var job = new TrainingJob(
            id: Guid.NewGuid(),
            projectId: projectId,
            name: "det-job",
            backend: BackendKeys.TorchSharpDetection,
            modelType: ModelTypeNames.Yolo26SDet,
            dataRoot: @"C:\data",
            outputRoot: @"C:\models",
            status: TrainingJobStatus.Succeeded,
            startedAt: DateTime.UtcNow.AddMinutes(-5),
            finishedAt: DateTime.UtcNow,
            bestModelPath: @"C:\models\best.torchsharp",
            lastModelPath: @"C:\models\last.torchsharp",
            logFile: @"C:\models\train.log",
            processId: null,
            configuration: new TrainingConfiguration(
                epochs: 20,
                batchSize: 8,
                learningRate: 0.001,
                optimizer: "sgd",
                validationRatio: 0.2,
                galleryId: Guid.NewGuid(),
                networkInputSize: 640,
                networkInputWidth: 640,
                networkInputHeight: 640,
                earlyStoppingPatience: 5,
                detectionOptions: new DetectionTrainingOptions(
                    NmsIouThreshold: 0.55,
                    EvaluationConfidence: 0.25,
                    MaxDetections: 240,
                    HorizontalFlip: true)),
            failureMessage: null);

        var existingTarget = TrainedModel.CreateNew(
            projectId,
            "old-det-model",
            ModelTypeNames.Yolo26SDet,
            TrainedModelStatus.Ready,
            note: "",
            classes: ["scratch"],
            checkpointPath: @"C:\models\old.torchsharp",
            maxDetections: 500);

        var mediator = new CapturingMediator(existingTarget);
        var handler = new SaveTrainedModelCommandHandler(
            new FakeTrainingJobRepository(job),
            new FakeArtifactService(@"C:\models\saved.torchsharp", @"C:\models\old.torchsharp"),
            new FakeProjectRepository(new Project(projectId, "DetProj", DateTimeOffset.UtcNow, type: ProjectTypes.Detection, labels: ["scratch"])),
            mediator);

        var createResult = await handler.Handle(
            new SaveTrainedModelCommand(projectId, job.Id, "new-det-model", TargetModelId: null),
            CancellationToken.None);
        Assert.True(createResult.IsSuccess, createResult.Error);
        Assert.NotNull(mediator.LastCreateCommand);
        Assert.Equal(240, mediator.LastCreateCommand!.MaxDetections);

        var overwriteResult = await handler.Handle(
            new SaveTrainedModelCommand(projectId, job.Id, "updated-det-model", TargetModelId: targetModelId),
            CancellationToken.None);
        Assert.True(overwriteResult.IsSuccess, overwriteResult.Error);
        Assert.NotNull(mediator.LastUpdateCommand);
        Assert.Equal(240, mediator.LastUpdateCommand!.MaxDetections);
    }

    private sealed class FakeModelRepository : IModelRepository
    {
        private readonly TrainedModel _model;
        public bool ThrowOnSave { get; set; }

        public FakeModelRepository(TrainedModel model) => _model = model;

        public Task<TrainedModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult<TrainedModel?>(_model);

        public Task<bool> ExistsWithNameAsync(Guid projectId, string name, Guid? excludeModelId = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task SaveAsync(Guid projectId, TrainedModel model, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave)
            {
                throw new InvalidOperationException("SQLite database write conflict");
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<TrainedModel>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TrainedModel>>([_model]);

        public Task<bool> DeleteAsync(Guid projectId, Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeArtifactService : ITrainedModelArtifactService
    {
        private readonly string _renamedPath;
        private readonly string _originalPath;
        public int ForwardRenameCalls { get; private set; }
        public int RollbackRenameCalls { get; private set; }

        public FakeArtifactService(string renamedPath, string originalPath)
        {
            _renamedPath = renamedPath;
            _originalPath = originalPath;
        }

        public Result<string> TryRenameCheckpointFiles(string checkpointPath, string newModelName)
        {
            if (checkpointPath.Contains("model_initial"))
            {
                ForwardRenameCalls++;
                return Result<string>.Success(_renamedPath);
            }

            RollbackRenameCalls++;
            return Result<string>.Success(_originalPath);
        }

        public Result<string> ResolveBestCheckpointPath(ModelArtifactCheckpointRequest request) => Result<string>.Success(_renamedPath);
        public Result<string> ResolveCheckpointPath(ModelArtifactCheckpointRequest request) => Result<string>.Success(_renamedPath);
        public Task<Result<IReadOnlyList<string>>> RecoverClassesAsync(ModelArtifactClassRecoveryRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<string>>.Success(["OK", "NG"]));
        public Task<Result<SavedTrainedModelArtifact>> SaveModelArtifactAsync(ModelArtifactSaveRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result<SavedTrainedModelArtifact>.Success(new SavedTrainedModelArtifact(_renamedPath, null)));
    }

    private sealed class FakeCheckpointFileService : IModelCheckpointFileService
    {
        public bool Exists(string? checkpointPath) => true;
        public bool TryGetDirectory(string checkpointPath, out string directory)
        {
            directory = @"C:\models";
            return true;
        }
        public string? Export(string checkpointPath, string exportRoot, string folderName) => exportRoot;
        public void DeleteFiles(string checkpointPath) { }
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        private readonly Project _project;
        public FakeProjectRepository(Project project) => _project = project;
        public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Project>>([_project]);
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Project?>(_project);
        public Task<Project> CreateAsync(string name, string description, string type, CancellationToken cancellationToken = default) => Task.FromResult(_project);
        public Task SaveAsync(Project project, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateDetailsAsync(Guid projectId, string name, string description, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ExistsByNameAsync(string name, Guid? excludeProjectId = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FakeTaskCapabilityResolver : ITaskCapabilityResolver
    {
        public Result ValidateExecution(string? projectType) => Result.Success();
        public Result ValidateModelExecution(string? modelType) => Result.Success();
        public Result<ProjectCapabilityProfile> ForProject(string? projectType) =>
            Result<ProjectCapabilityProfile>.Success(ProjectCapabilityCatalog.Resolve(ProjectWorkflowKind.Classification));
        public ProjectCapabilityProfile ForProject(ProjectWorkflowKind workflow) =>
            ProjectCapabilityCatalog.Resolve(workflow);
        public Result<ResolvedTaskProfile> ForProjectAndModel(string? projectType, string? modelType) =>
            Result<ResolvedTaskProfile>.Success(new ResolvedTaskProfile
            {
                Project = ProjectCapabilityCatalog.Resolve(ProjectWorkflowKind.Classification),
                Model = new ModelCapabilities(ModelFamily.Classification, BackendKeys.TorchSharpClassification, BackendKeys.InferenceTorchSharpClassification, ArtifactHandlerKeys.Classification, true),
                ModelType = modelType ?? "resnet18",
                InferenceBackendKey = BackendKeys.InferenceTorchSharpClassification,
                ArtifactHandlerKey = ArtifactHandlerKeys.Classification,
                EffectiveExportKind = ExportKind.ClassificationFolders,
                InferenceKind = InferenceKind.ClassificationServe,
                WriteBackKind = InferenceWriteBackKind.ImageClassTop1,
                MetricProfile = MetricProfileKind.ClassificationAccuracy,
                RuntimeSessionKind = RuntimeSessionKind.InProcess,
                UsesNetworkInputSize = true,
                NetworkInputSizePolicy = NetworkInputSizePolicy.ClassificationProduct,
                ShowsClassificationAugmentation = true
            });
    }

    private sealed class FakeTrainingJobRepository : ITrainingJobRepository
    {
        private readonly TrainingJob _job;
        public FakeTrainingJobRepository(TrainingJob job) => _job = job;
        public Task<TrainingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<TrainingJob?>(_job);
        public Task<IReadOnlyList<TrainingJob>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TrainingJob>>([_job]);
        public Task<IReadOnlyList<TrainingJob>> GetByProjectIdAsync(Guid projectId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TrainingJob>>([_job]);
        public Task<IReadOnlyList<TrainingJob>> GetRunningAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TrainingJob>>([]);
        public Task<IReadOnlyList<TrainingJob>> GetByStatusesAsync(IReadOnlyList<TrainingJobStatus> statuses, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TrainingJob>>([_job]);
        public Task SaveAsync(TrainingJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> SaveIfStatusAsync(TrainingJob job, IReadOnlyCollection<TrainingJobStatus> allowedCurrent, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CapturingMediator : IMediator
    {
        private readonly TrainedModel _existingModel;
        public CreateModelCommand? LastCreateCommand { get; private set; }
        public UpdateModelCommand? LastUpdateCommand { get; private set; }

        public CapturingMediator(TrainedModel existingModel) => _existingModel = existingModel;

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object response = request switch
            {
                GetModelByIdQuery => Result<TrainedModel?>.Success(_existingModel),
                CreateModelCommand create => RecordCreate(create),
                UpdateModelCommand update => RecordUpdate(update),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return Task.FromResult((TResponse)response);
        }

        private Result<Guid> RecordCreate(CreateModelCommand command)
        {
            LastCreateCommand = command;
            return Result<Guid>.Success(Guid.NewGuid());
        }

        private Result RecordUpdate(UpdateModelCommand command)
        {
            LastUpdateCommand = command;
            return Result.Success();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => Task.CompletedTask;
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default) where TNotification : INotification => Task.CompletedTask;
    }
}
