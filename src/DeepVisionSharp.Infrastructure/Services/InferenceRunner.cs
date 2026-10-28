using DeepVisionSharp.Application.Inference;
using DeepVisionSharp.Application.Ports;
using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Repositories;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class InferenceRunner : IInferenceRunner
{
    private const string StoppedMessage = "推理已被停止。";

    private readonly InferenceBackendRegistry _backendRegistry;
    private readonly ITaskCapabilityResolver _capabilities;
    private readonly IModelRepository _models;
    private readonly IProjectRepository _projects;
    private readonly IInferenceRuntime _runtime;

    public InferenceRunner(InferenceBackendRegistry backendRegistry, ITaskCapabilityResolver capabilities,
        IModelRepository models, IProjectRepository projects, IInferenceRuntime runtime)
    {
        _backendRegistry = backendRegistry;
        _capabilities = capabilities;
        _models = models;
        _projects = projects;
        _runtime = runtime;
    }

    public Task<InferenceResult> RunSingleAsync(
        string modelPath,
        string imagePath,
        InferenceConfig config,
        string modelType,
        CancellationToken cancellationToken)
        => Task.Run(() => RunSingleCoreAsync(modelPath, imagePath, config, modelType, cancellationToken), cancellationToken);

    private async Task<InferenceResult> RunSingleCoreAsync(string modelPath, string imagePath,
        InferenceConfig config, string modelType, CancellationToken cancellationToken)
    {
        var execution = _capabilities.ValidateModelExecution(modelType);
        cancellationToken.ThrowIfCancellationRequested();
        if (execution.IsFailure)
        {
            return new InferenceResult { Success = false, Error = execution.Error };
        }
        var projectError = await ValidateModelProjectAsync(config, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (projectError is not null) return new InferenceResult { Success = false, Error = projectError };
        var context = new InferenceBackendContext(modelPath, imagePath, config, modelType);
        var backend = _backendRegistry.Resolve(context);
        if (backend.IsFailure || backend.Value is null)
        {
            return new InferenceResult
            {
                Success = false,
                Error = backend.Error ?? "不支持的推理模型类型"
            };
        }

        // Only the backend call is registered: validation above holds no native resources, and the
        // runtime needs to know exactly what a shutdown or a training hand-off has to wait for.
        InferenceResult result;
        try
        {
            result = await _runtime
                .ExecuteAsync(
                    InferenceOperationKinds.Inference,
                    token => backend.Value.RunSingleAsync(context, token),
                    cancellationToken,
                    config.OwnerKey)
                .ConfigureAwait(false);
        }
        catch (InferenceRuntimeUnavailableException ex)
        {
            return new InferenceResult { Success = false, Error = ex.Message };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The runtime cancelled the operation (shutdown), not the caller.
            return new InferenceResult { Success = false, Error = StoppedMessage };
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    public Task<InferenceWarmupResult> WarmupAsync(
        string modelPath,
        InferenceConfig config,
        string modelType,
        CancellationToken cancellationToken)
        => Task.Run(() => WarmupCoreAsync(modelPath, config, modelType, cancellationToken), cancellationToken);

    private async Task<InferenceWarmupResult> WarmupCoreAsync(string modelPath, InferenceConfig config,
        string modelType, CancellationToken cancellationToken)
    {
        // Image path unused for load/preflight; placeholder keeps context shape stable.
        var execution = _capabilities.ValidateModelExecution(modelType);
        cancellationToken.ThrowIfCancellationRequested();
        if (execution.IsFailure)
        {
            return InferenceWarmupResult.Fail(execution.Error!);
        }
        var projectError = await ValidateModelProjectAsync(config, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (projectError is not null) return InferenceWarmupResult.Fail(projectError);
        var context = new InferenceBackendContext(modelPath, string.Empty, config, modelType);
        var backend = _backendRegistry.Resolve(context);
        if (backend.IsFailure || backend.Value is null)
        {
            return InferenceWarmupResult.Fail(backend.Error ?? "不支持的推理模型类型");
        }

        InferenceWarmupResult result;
        try
        {
            result = await _runtime
                .ExecuteAsync(
                    InferenceOperationKinds.Warmup,
                    token => backend.Value.WarmupAsync(context, token),
                    cancellationToken,
                    config.OwnerKey)
                .ConfigureAwait(false);
        }
        catch (InferenceRuntimeUnavailableException ex)
        {
            return InferenceWarmupResult.Fail(ex.Message);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return InferenceWarmupResult.Fail(StoppedMessage);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<string?> ValidateModelProjectAsync(InferenceConfig config, CancellationToken cancellationToken)
    {
        if (config.ModelId is not Guid modelId) return null;
        var model = await _models.GetByIdAsync(modelId, cancellationToken).ConfigureAwait(false);
        if (model is null) return null;
        var project = await _projects.GetByIdAsync(model.ProjectId, cancellationToken).ConfigureAwait(false);
        return project is null ? "模型所属项目不存在。" : _capabilities.ValidateExecution(project.Type).Error;
    }

    public Task StopAsync(string? ownerKey, string? reason, CancellationToken cancellationToken)
        => _runtime.StopAsync(ownerKey, reason, cancellationToken);
}
