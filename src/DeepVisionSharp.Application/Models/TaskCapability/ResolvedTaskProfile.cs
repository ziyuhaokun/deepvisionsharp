using Microsoft.Extensions.Logging;
using DeepVisionSharp.Application.Galleries;
using DeepVisionSharp.Domain.Common;
using DeepVisionSharp.Domain.ValueObjects;

namespace DeepVisionSharp.Application.Models;

public enum InferenceKind
{
    None,
    ClassificationServe
}

public enum MetricProfileKind
{
    None,
    ClassificationAccuracy
}

public enum InferenceWriteBackKind
{
    None,
    ImageClassTop1
}

public enum RuntimeSessionKind
{
    None,
    InProcess
}

public sealed record ResolvedTaskProfile
{
    public required ProjectCapabilityProfile Project { get; init; }
    public required ModelCapabilities Model { get; init; }
    public required string ModelType { get; init; }
    public string TrainingBackendKey => Model.TrainingBackendKey;
    public string? InferenceBackendKey { get; init; }
    public required string ArtifactHandlerKey { get; init; }
    public required ExportKind EffectiveExportKind { get; init; }
    public required InferenceKind InferenceKind { get; init; }
    public required InferenceWriteBackKind WriteBackKind { get; init; }
    public required MetricProfileKind MetricProfile { get; init; }
    public required RuntimeSessionKind RuntimeSessionKind { get; init; }
    public bool UsesNetworkInputSize { get; init; }

    /// <summary>When <see cref="UsesNetworkInputSize"/>, product range for network input edge length.</summary>
    public NetworkInputSizePolicy? NetworkInputSizePolicy { get; init; }

    public bool ShowsClassificationAugmentation { get; init; }

    public ModelFamily ModelFamily => Model.Family;
    public bool UsesInProcessTraining => Model.UsesInProcessTraining;
}

public interface ITaskCapabilityResolver
{
    Result ValidateExecution(string? projectType);
    Result ValidateModelExecution(string? modelType);
    Result<ProjectCapabilityProfile> ForProject(string? projectType);
    ProjectCapabilityProfile ForProject(ProjectWorkflowKind workflow);
    Result<ResolvedTaskProfile> ForProjectAndModel(string? projectType, string? modelType);
}

internal sealed class TaskCapabilityResolver : ITaskCapabilityResolver
{
    private readonly ILogger<TaskCapabilityResolver> _logger;

    public TaskCapabilityResolver(ILogger<TaskCapabilityResolver> logger)
    {
        _logger = logger;
    }

    public Result ValidateExecution(string? projectType)
    {
        var profile = ForProject(projectType);
        return profile.IsFailure
            ? Result.Failure(profile.Error ?? "项目类型无效。")
            : Result.Success();
    }

    public Result ValidateModelExecution(string? modelType)
    {
        if (!ModelCapabilityCatalog.TryResolve(modelType, out var model))
        {
            return Result.Failure($"不支持的模型类型：{modelType}");
        }

        return model.Family == ModelFamily.Classification
            ? Result.Success()
            : Result.Failure("当前仅开放分类模型。");
    }

    public Result<ProjectCapabilityProfile> ForProject(string? projectType)
    {
        if (!ProjectTypes.IsValid(projectType))
        {
            return Result<ProjectCapabilityProfile>.Failure(
                string.IsNullOrWhiteSpace(projectType)
                    ? "项目类型不能为空。"
                    : $"不支持的项目类型：{projectType.Trim()}");
        }

        return Result<ProjectCapabilityProfile>.Success(
            ProjectCapabilityCatalog.Resolve(ProjectWorkflow.Resolve(projectType)));
    }

    public ProjectCapabilityProfile ForProject(ProjectWorkflowKind workflow) =>
        ProjectCapabilityCatalog.Resolve(workflow);

    public Result<ResolvedTaskProfile> ForProjectAndModel(string? projectType, string? modelType)
    {
        var projectResult = ForProject(projectType);
        if (projectResult.IsFailure || projectResult.Value is null)
        {
            return Result<ResolvedTaskProfile>.Failure(projectResult.Error ?? "项目类型无效。");
        }

        if (string.IsNullOrWhiteSpace(modelType))
        {
            return Result<ResolvedTaskProfile>.Failure("模型类型不能为空。");
        }

        if (!ModelTypeNames.TryNormalize(modelType, out var normalizedModelType)
            || !ModelCapabilityCatalog.TryResolve(normalizedModelType, out var model))
        {
            return Result<ResolvedTaskProfile>.Failure($"不支持的模型类型：{modelType.Trim()}");
        }

        var compatibility = TaskCompatibilityCatalog.Validate(projectResult.Value.Workflow, model.Family);
        if (compatibility.IsFailure)
        {
            _logger.LogWarning(
                "Task capability rejected: workflow={Workflow}, modelType={ModelType}, family={Family}, error={Error}",
                projectResult.Value.Workflow,
                normalizedModelType,
                model.Family,
                compatibility.Error);
            return Result<ResolvedTaskProfile>.Failure(compatibility.Error ?? "项目与模型不兼容。");
        }

        var overlay = ResolveOverlay(projectResult.Value.Workflow, model.Family);
        var inputSizePolicy = overlay.UsesNetworkInputSize
            ? NetworkInputSizePolicy.ForModelFamily(model.Family)
            : null;
        var profile = new ResolvedTaskProfile
        {
            Project = projectResult.Value,
            Model = model,
            ModelType = normalizedModelType,
            InferenceBackendKey = overlay.InferenceBackendKey,
            ArtifactHandlerKey = overlay.ArtifactHandlerKey,
            EffectiveExportKind = overlay.EffectiveExportKind,
            InferenceKind = overlay.InferenceKind,
            WriteBackKind = overlay.WriteBackKind,
            MetricProfile = overlay.MetricProfile,
            RuntimeSessionKind = overlay.RuntimeSessionKind,
            UsesNetworkInputSize = overlay.UsesNetworkInputSize,
            NetworkInputSizePolicy = inputSizePolicy,
            ShowsClassificationAugmentation = overlay.ShowsClassificationAugmentation
        };

        _logger.LogDebug(
            "Task capability resolved: workflow={Workflow}, modelType={ModelType}, family={Family}, export={Export}, inference={Inference}, metric={Metric}, backend={Backend}",
            profile.Project.Workflow,
            profile.ModelType,
            profile.ModelFamily,
            profile.EffectiveExportKind,
            profile.InferenceKind,
            profile.MetricProfile,
            profile.TrainingBackendKey);
        return Result<ResolvedTaskProfile>.Success(profile);
    }

    private static TaskOverlay ResolveOverlay(ProjectWorkflowKind workflow, ModelFamily family)
    {
        if (workflow != ProjectWorkflowKind.Classification
            || family != ModelFamily.Classification)
        {
            throw new InvalidOperationException($"\u7f3a\u5c11\u4efb\u52a1\u80fd\u529b\u53e0\u52a0\uff1a{workflow} + {family}");
        }

        return new TaskOverlay(
            ExportKind.ClassificationFolders,
            InferenceKind.ClassificationServe,
            InferenceWriteBackKind.ImageClassTop1,
            MetricProfileKind.ClassificationAccuracy,
            RuntimeSessionKind.InProcess,
            BackendKeys.InferenceTorchSharpClassification,
            ArtifactHandlerKeys.Classification,
            UsesNetworkInputSize: true,
            ShowsClassificationAugmentation: true);
    }

    private sealed record TaskOverlay(
        ExportKind EffectiveExportKind,
        InferenceKind InferenceKind,
        InferenceWriteBackKind WriteBackKind,
        MetricProfileKind MetricProfile,
        RuntimeSessionKind RuntimeSessionKind,
        string? InferenceBackendKey,
        string ArtifactHandlerKey,
        bool UsesNetworkInputSize,
        bool ShowsClassificationAugmentation);
}
