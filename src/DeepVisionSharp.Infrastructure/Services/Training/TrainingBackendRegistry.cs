using DeepVisionSharp.Application.Models;
using DeepVisionSharp.Domain.Common;

namespace DeepVisionSharp.Infrastructure.Services;

internal sealed class TrainingBackendRegistry : KeyedRegistry<ITrainingBackend>
{
    public TrainingBackendRegistry(IEnumerable<ITrainingBackend> backends)
        : base(backends, item => item.BackendKey, nameof(TrainingBackendRegistry))
    {
    }

    public IReadOnlyList<ITrainingBackend> Backends => Items;

    protected override string EmptyKeyMessage => "训练后端键不能为空";

    protected override string GetNotRegisteredMessage(string key) => $"未注册的训练后端：{key}";

    /// <summary>
    /// Resolve via capability catalog key, then validate <see cref="ITrainingBackend.CanHandle"/>.
    /// </summary>
    public Result<ITrainingBackend> Resolve(string? modelType, string? executionEngine = null)
    {
        if (string.IsNullOrWhiteSpace(modelType))
        {
            return Result<ITrainingBackend>.Failure("训练模型类型不能为空");
        }

        if (!ModelCapabilityCatalog.TryResolve(modelType, executionEngine, out var capabilities))
        {
            return Result<ITrainingBackend>.Failure($"不支持的训练模型类型：{modelType.Trim()}");
        }

        var byKey = ResolveByKey(capabilities.TrainingBackendKey);
        if (byKey.IsFailure || byKey.Value is null)
        {
            return byKey;
        }

        if (!byKey.Value.CanHandle(modelType))
        {
            return Result<ITrainingBackend>.Failure(
                $"训练后端 {capabilities.TrainingBackendKey} 不支持模型类型：{modelType.Trim()}");
        }

        return byKey;
    }
}
