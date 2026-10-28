using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using DeepVisionSharp.Application.Galleries;
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
}
